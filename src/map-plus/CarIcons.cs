using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Helpers;
using Model;
using RollingStock;
using TMPro;
using UI;
using UI.Map;
using UnityEngine;
using UnityEngine.UI;

namespace QbanRambo.MapPlus;

internal sealed class CarIcons : MonoBehaviour
{
  private const float RefreshSeconds = 1f;
  private const float IconHeight = 100f;
  private const float ReferenceCarLength = 11f;
  private const float CarScale = 0.8f;
  private const float FontSize = 19f;
  private const int SpriteSize = 128;
  private const int BodyLengthInset = 4;
  private const int BodyWidthInset = 38;
  private const float BodyLength = (SpriteSize - 2f * BodyLengthInset) / SpriteSize;
  private const float BodyWidth = (SpriteSize - 2f * BodyWidthInset) / SpriteSize;
  private const float LabelMargin = 0.9f;
  private const float CouplerGap = 1f;
  private const int AlertSpriteSize = 64;
  private const float AlertOutline = 6f;
  private const float AlertScale = 1.3f;

  internal const int LocomotiveOrderOffset = 3;
  private const float PulsePeriod = 1f;
  private const float PulseGrowth = 0.35f;
  private const float PulseDim = 0.35f;
  private const string BadgeName = "Map Plus Alert";

  internal static int ZoomVersion;

  internal static readonly FieldInfo MapIconField = typeof(Car).GetField("MapIcon", BindingFlags.Instance | BindingFlags.NonPublic);
  private static readonly MethodInfo FixTextRotationMethod = typeof(MapIcon).GetMethod("FixTextRotation", BindingFlags.Instance | BindingFlags.NonPublic);
  private static Sprite _wideSprite;
  private static Sprite _tallSprite;
  private static Sprite _alertSprite;

  private readonly Dictionary<Car, CarIcon> _icons = new();
  private readonly HashSet<Canvas> _raised = new();
  private readonly Dictionary<BaseLocomotive, Image> _locomotiveBadges = new();
  private readonly HashSet<Image> _pulsing = new();
  private readonly Dictionary<MapIcon, Action> _locomotiveClicks = new();
  private int _baseOrder;
  private float _nextRefresh;
  private int _seenZoomVersion;
  private bool _logged;

  private void OnEnable()
  {
    MapPlusMod.Cars.Changed += OnOptionChanged;
    MapPlusMod.CarLabels.Changed += OnOptionChanged;
    MapPlusMod.CarAlerts.Changed += OnOptionChanged;
    MapPlusMod.TrainAlerts.Changed += OnOptionChanged;
    if (MapIconField == null) MapPlusMod.Log?.Error("Car.MapIcon not found; this Railroader version isn't supported");
  }

  private void OnDisable()
  {
    MapPlusMod.Cars.Changed -= OnOptionChanged;
    MapPlusMod.CarLabels.Changed -= OnOptionChanged;
    MapPlusMod.CarAlerts.Changed -= OnOptionChanged;
    MapPlusMod.TrainAlerts.Changed -= OnOptionChanged;
  }

  private void OnDestroy()
  {
    foreach (var icon in _icons.Values) icon.Remove();
    _icons.Clear();
    foreach (var badge in _locomotiveBadges.Values)
    {
      if (badge != null) Destroy(badge.gameObject);
    }
    _locomotiveBadges.Clear();
    TrainAlerts.Reset();
    foreach (var canvas in _raised)
    {
      if (canvas != null) canvas.sortingOrder = _baseOrder;
    }
    _raised.Clear();
    foreach (var entry in _locomotiveClicks)
    {
      if (entry.Key != null) entry.Key.OnClick = entry.Value;
    }
    _locomotiveClicks.Clear();
  }

  internal static void ClickCar(Car car)
  {
    try
    {
      if (car == null) return;
      var controller = TrainController.Shared;
      var target = car is BaseLocomotive ? car : Consist.Of(car).OfType<BaseLocomotive>().FirstOrDefault() ?? car;
      if (controller != null) controller.SelectedCar = target;
      if (!GameInput.IsShiftDown) CarPickable.HandleShowInspector(car);
    }
    catch (Exception exception)
    {
      MapPlusMod.Log?.Error(exception, $"Couldn't select {car?.DisplayName}");
    }
  }

  private void HookClick(BaseLocomotive locomotive)
  {
    if (MapIconField.GetValue(locomotive) is not MapIcon icon || icon == null || _locomotiveClicks.ContainsKey(icon)) return;
    _locomotiveClicks[icon] = icon.OnClick;
    icon.OnClick = () => ClickCar(locomotive);
  }

  private void OnOptionChanged(bool _) => _nextRefresh = 0f;

  private void Update()
  {
    if (MapIconField == null || Time.unscaledTime < _nextRefresh) return;
    _nextRefresh = Time.unscaledTime + RefreshSeconds;
    try
    {
      Refresh();
    }
    catch (Exception exception)
    {
      LogOnce(exception, "Couldn't refresh car icons");
    }
  }

  private void LateUpdate()
  {
    Pulse();
    if (_seenZoomVersion == ZoomVersion) return;
    _seenZoomVersion = ZoomVersion;
    try
    {
      var showLabels = MapPlusMod.CarLabels.IsOn;
      foreach (var icon in _icons.Values)
      {
        if (icon.Car != null) icon.UpdateLabel(showLabels);
      }
    }
    catch (Exception exception)
    {
      LogOnce(exception, "Couldn't update car labels after zooming");
    }
  }

  private void Refresh()
  {
    var controller = TrainController.Shared;
    if (controller == null) return;
    var prefab = controller.locomotiveMapIconPrefab;
    var prefabCanvas = prefab != null ? prefab.GetComponent<Canvas>() : null;
    _baseOrder = prefabCanvas != null ? prefabCanvas.sortingOrder : 0;
    var mapShown = IsMapShown();
    foreach (var car in controller.Cars)
    {
      if (car is BaseLocomotive locomotive)
      {
        Raise(locomotive);
        HookClick(locomotive);
        if (mapShown) FixText(MapIconField.GetValue(locomotive) as MapIcon);
        UpdateLocomotiveBadge(locomotive, mapShown);
        continue;
      }
      if (car == null || _icons.ContainsKey(car)) continue;
      try
      {
        var icon = CarIcon.Create(car, controller.locomotiveMapIconPrefab);
        if (icon != null) _icons[car] = icon;
      }
      catch (Exception exception)
      {
        LogOnce(exception, $"Couldn't add a map icon for {car.DisplayName}");
      }
    }
    var showCars = MapPlusMod.Cars.IsOn;
    var showLabels = MapPlusMod.CarLabels.IsOn;
    var showAlerts = MapPlusMod.CarAlerts.IsOn && mapShown;
    var inTrains = showAlerts ? CarsInTrains(controller) : null;
    foreach (var car in _icons.Keys.ToList())
    {
      var icon = _icons[car];
      if (car == null || icon.Icon == null)
      {
        _icons.Remove(car);
        continue;
      }
      try
      {
        icon.Refresh(showCars, showLabels, mapShown, showAlerts ? CarInfo.Alert(car, inTrains.Contains(car)) : null);
        if (mapShown) FixText(icon.Icon);
      }
      catch (Exception exception)
      {
        LogOnce(exception, $"Couldn't update the map icon of {car.DisplayName}");
      }
    }
  }

  private void UpdateLocomotiveBadge(BaseLocomotive locomotive, bool mapShown)
  {
    try
    {
      if (MapIconField.GetValue(locomotive) is not MapIcon icon || icon == null) return;
      var alert = MapPlusMod.TrainAlerts.IsOn ? TrainAlerts.BadgeColor(locomotive) : null;
      var color = mapShown ? alert : null;
      if (!_locomotiveBadges.TryGetValue(locomotive, out var badge) || badge == null || !badge.transform.IsChildOf(icon.transform))
      {
        if (!color.HasValue) return;
        var image = icon.GetComponentsInChildren<Image>(true).FirstOrDefault(candidate => candidate.gameObject.name != BadgeName);
        badge = CarIcon.CreateBadge(icon, image, icon.GetComponent<Canvas>(), _baseOrder + LocomotiveOrderOffset + 1);
        if (badge == null) return;
        _locomotiveBadges[locomotive] = badge;
      }
      if (badge.gameObject.activeSelf != color.HasValue) badge.gameObject.SetActive(color.HasValue);
      if (color.HasValue) badge.color = color.Value;
      if (color == TrainAlerts.DangerColor)
      {
        _pulsing.Add(badge);
      }
      else if (_pulsing.Remove(badge))
      {
        badge.rectTransform.localScale = Vector3.one;
      }
    }
    catch (Exception exception)
    {
      LogOnce(exception, $"Couldn't update the alert of {locomotive.DisplayName}");
    }
  }

  private void Pulse()
  {
    if (_pulsing.Count == 0) return;
    _pulsing.RemoveWhere(badge => badge == null);
    var phase = Mathf.Repeat(Time.unscaledTime, PulsePeriod) / PulsePeriod;
    var beat = Mathf.Sin(phase * Mathf.PI);
    var scale = Vector3.one * (1f + PulseGrowth * beat);
    var color = Color.Lerp(TrainAlerts.DangerColor, Color.white, PulseDim * beat);
    foreach (var badge in _pulsing)
    {
      if (!badge.gameObject.activeInHierarchy) continue;
      badge.rectTransform.localScale = scale;
      badge.color = color;
    }
  }

  private static void FixText(MapIcon icon)
  {
    if (icon != null && icon.gameObject.activeInHierarchy) FixTextRotationMethod?.Invoke(icon, null);
  }

  private void Raise(BaseLocomotive locomotive)
  {
    if (MapIconField.GetValue(locomotive) is not MapIcon icon || icon == null) return;
    var canvas = icon.GetComponent<Canvas>();
    if (canvas == null || canvas.sortingOrder == _baseOrder + LocomotiveOrderOffset) return;
    canvas.sortingOrder = _baseOrder + LocomotiveOrderOffset;
    _raised.Add(canvas);
  }

  private static HashSet<Car> CarsInTrains(TrainController controller)
  {
    var cars = new HashSet<Car>();
    foreach (var locomotive in controller.Cars.OfType<BaseLocomotive>())
    {
      if (locomotive == null || cars.Contains(locomotive)) continue;
      cars.UnionWith(Consist.Of(locomotive));
    }
    return cars;
  }

  private static bool IsMapShown()
  {
    var builder = MapBuilder.Shared;
    return builder != null && builder.mapCamera != null && builder.mapCamera.gameObject.activeInHierarchy;
  }

  private void LogOnce(Exception exception, string message)
  {
    if (_logged) return;
    _logged = true;
    MapPlusMod.Log?.Error(exception, message);
  }

  private static Sprite CarSprite(bool alongX)
  {
    if (alongX) return _wideSprite != null ? _wideSprite : _wideSprite = CreateSprite(true);
    return _tallSprite != null ? _tallSprite : _tallSprite = CreateSprite(false);
  }

  private static Sprite CreateSprite(bool alongX)
  {
    var pixels = new Color32[SpriteSize * SpriteSize];
    for (var y = 0; y < SpriteSize; y++)
    {
      for (var x = 0; x < SpriteSize; x++)
      {
        pixels[y * SpriteSize + x] = alongX ? BodyPixel(x, y) : BodyPixel(y, x);
      }
    }
    return Sprites.Create(SpriteSize, pixels);
  }

  private static Sprite AlertSprite => _alertSprite != null ? _alertSprite : _alertSprite = CreateAlertSprite();

  private static Sprite CreateAlertSprite()
  {
    var pixels = new Color32[AlertSpriteSize * AlertSpriteSize];
    var radius = AlertSpriteSize / 2f;
    var center = new Vector2(radius, radius);
    for (var y = 0; y < AlertSpriteSize; y++)
    {
      for (var x = 0; x < AlertSpriteSize; x++)
      {
        var distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
        var alpha = (byte)(Mathf.Clamp01(radius - distance) * 255f);
        var shade = distance > radius - AlertOutline ? (byte)20 : (byte)255;
        pixels[y * AlertSpriteSize + x] = new Color32(shade, shade, shade, alpha);
      }
    }
    return Sprites.Create(AlertSpriteSize, pixels);
  }

  private static Color32 BodyPixel(int along, int across)
  {
    const int last = SpriteSize - 1;
    var edge = Mathf.Min(Mathf.Min(along, last - along) - BodyLengthInset, Mathf.Min(across, last - across) - BodyWidthInset);
    if (edge < 0) return new Color32(255, 255, 255, 0);
    if (edge == 0) return new Color32(0, 0, 0, 30);
    if (edge <= 3) return new Color32(0, 0, 0, 114);
    if (edge == 4) return new Color32(170, 170, 170, 155);
    return new Color32(255, 255, 255, 255);
  }

  private sealed class CarIcon
  {
    private string _label;
    private Vector3? _baseTextScale;
    private Vector2? _textSize;
    private Color? _color;
    private Color? _alert;

    private CarIcon(Car car, MapIcon icon, Image image, TMP_Text text, Image badge)
    {
      Car = car;
      Icon = icon;
      Image = image;
      Text = text;
      Badge = badge;
    }

    public Car Car { get; }
    public MapIcon Icon { get; }
    private Image Image { get; }
    private TMP_Text Text { get; }
    private Image Badge { get; }

    public static CarIcon Create(Car car, MapIcon prefab)
    {
      if (prefab == null || MapIconField.GetValue(car) != null) return null;
      var icon = UnityEngine.Object.Instantiate(prefab, car.transform);
      icon.name = "Map Plus Car Icon";
      var canvas = icon.GetComponent<Canvas>();
      var prefabCanvas = prefab.GetComponent<Canvas>();
      var order = prefabCanvas != null ? prefabCanvas.sortingOrder : 0;
      if (canvas != null) canvas.sortingOrder = order;
      PlaceAtCar(icon, car);
      FixText(icon);
      var image = icon.GetComponentInChildren<Image>(true);
      var text = icon.GetComponentInChildren<TMP_Text>(true);
      if (image != null && text != null && text.transform.IsChildOf(image.transform)) text.transform.SetParent(image.transform.parent, true);
      if (image != null)
      {
        var along = icon.transform.up;
        var alongX = Mathf.Abs(Vector3.Dot(image.transform.right, along)) > Mathf.Abs(Vector3.Dot(image.transform.up, along));
        image.sprite = CarSprite(alongX);
        image.type = Image.Type.Simple;
        image.preserveAspect = false;
        var scale = image.transform.localScale * CarScale;
        var length = car.carLength / ReferenceCarLength;
        image.transform.localScale = alongX ? new Vector3(scale.x * length, scale.y, scale.z) : new Vector3(scale.x, scale.y * length, scale.z);
      }
      if (text != null)
      {
        text.enableAutoSizing = false;
        text.fontSize = FontSize;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.horizontalAlignment = HorizontalAlignmentOptions.Left;
        text.autoSizeTextContainer = true;
        text.transform.localPosition = Vector3.zero;
        if (text.gameObject != icon.gameObject)
        {
          var textCanvas = text.GetComponent<Canvas>();
          if (textCanvas == null) textCanvas = text.gameObject.AddComponent<Canvas>();
          textCanvas.overrideSorting = true;
          if (canvas != null) textCanvas.sortingLayerID = canvas.sortingLayerID;
          textCanvas.sortingOrder = order + 1;
        }
      }
      var badge = CreateBadge(icon, image, canvas, order + 2);
      icon.OnClick = () => Click(car);
      MapIconField.SetValue(car, icon);
      return new CarIcon(car, icon, image, text, badge);
    }

    private static void PlaceAtCar(MapIcon icon, Car car)
    {
      var front = car.LastBodyPosition[0];
      var rear = car.LastBodyPosition[1];
      var along = front - rear;
      if (along.sqrMagnitude < 0.01f)
      {
        icon.transform.SetPositionAndRotation(car.transform.position + Vector3.up * IconHeight, Quaternion.Euler(-90f, car.transform.rotation.eulerAngles.y, 0f));
        return;
      }
      var position = WorldTransformer.GameToWorld(Vector3.Lerp(front, rear, 0.5f));
      icon.transform.SetPositionAndRotation(position + Vector3.up * IconHeight, Quaternion.Euler(-90f, Quaternion.LookRotation(along).eulerAngles.y, 0f));
    }

    internal static Image CreateBadge(MapIcon icon, Image image, Canvas canvas, int order)
    {
      if (image == null) return null;
      var root = icon.transform;
      var size = image.rectTransform.rect.size;
      var length = LocalExtent(root, image.transform, size, Vector3.up) * BodyLength;
      var width = LocalExtent(root, image.transform, size, Vector3.right) * BodyWidth;
      var center = root.InverseTransformPoint(image.rectTransform.TransformPoint(image.rectTransform.rect.center));
      var badgeObject = new GameObject(BadgeName, typeof(RectTransform)) { layer = icon.gameObject.layer };
      var badgeTransform = (RectTransform)badgeObject.transform;
      badgeTransform.SetParent(root, false);
      badgeTransform.sizeDelta = Vector2.one * (width * AlertScale);
      badgeTransform.localPosition = center + Vector3.up * (length * 0.5f);
      var badgeCanvas = badgeObject.AddComponent<Canvas>();
      badgeCanvas.overrideSorting = true;
      if (canvas != null) badgeCanvas.sortingLayerID = canvas.sortingLayerID;
      badgeCanvas.sortingOrder = order;
      var badge = badgeObject.AddComponent<Image>();
      badge.sprite = AlertSprite;
      badge.raycastTarget = false;
      badgeObject.SetActive(false);
      return badge;
    }

    private static float LocalExtent(Transform root, Transform transform, Vector2 size, Vector3 axis) =>
      Mathf.Abs(Vector3.Dot(root.InverseTransformVector(transform.TransformVector(size.x, 0f, 0f)), axis)) + Mathf.Abs(Vector3.Dot(root.InverseTransformVector(transform.TransformVector(0f, size.y, 0f)), axis));

    public void Refresh(bool showCars, bool showLabels, bool mapShown, Color? alert)
    {
      var visible = showCars && !Car.IsInBardo;
      if (Icon.gameObject.activeSelf != visible) Icon.gameObject.SetActive(visible);
      if (!visible || !mapShown) return;
      if (Text != null)
      {
        var label = Label(Car);
        if (label != _label)
        {
          Icon.SetText(label);
          if (FitLabel(label)) _label = label;
        }
        UpdateLabel(showLabels);
      }
      if (Image != null)
      {
        var color = CarInfo.MapColor(Car);
        if (color != _color)
        {
          _color = color;
          Image.color = color;
        }
      }
      if (Badge != null && alert != _alert)
      {
        _alert = alert;
        Badge.gameObject.SetActive(alert.HasValue);
        if (alert.HasValue) Badge.color = alert.Value;
      }
    }

    public void UpdateLabel(bool showLabels)
    {
      if (Text == null || Icon == null || !Icon.gameObject.activeSelf) return;
      var shown = showLabels && (_textSize == null || Extent(Text.transform, _textSize.Value, Icon.transform.up) <= Car.carLength + CouplerGap);
      if (Text.gameObject.activeSelf != shown) Text.gameObject.SetActive(shown);
    }

    private bool FitLabel(string label)
    {
      if (_baseTextScale == null) _baseTextScale = Text.transform.localScale * CarScale;
      Text.transform.localScale = _baseTextScale.Value;
      var size = Text.GetPreferredValues(label);
      var along = Icon.transform.up;
      var across = Icon.transform.right;
      var textLength = Extent(Text.transform, size, along);
      var textWidth = Extent(Text.transform, size, across);
      if (textLength <= 0f || textWidth <= 0f) return false;
      _textSize = size;
      if (Image == null) return true;
      var boxSize = Image.rectTransform.rect.size;
      var fit = Mathf.Min(Extent(Image.transform, boxSize, along) * BodyLength * LabelMargin / textLength, Extent(Image.transform, boxSize, across) * BodyWidth / textWidth);
      Text.transform.localScale = _baseTextScale.Value * Mathf.Min(1f, fit);
      return true;
    }

    private static float Extent(Transform transform, Vector2 size, Vector3 axis) =>
      Mathf.Abs(Vector3.Dot(transform.TransformVector(size.x, 0f, 0f), axis)) + Mathf.Abs(Vector3.Dot(transform.TransformVector(0f, size.y, 0f), axis));

    public void Remove()
    {
      if (Car != null && ReferenceEquals(MapIconField.GetValue(Car), Icon)) MapIconField.SetValue(Car, null);
      if (Icon != null) UnityEngine.Object.Destroy(Icon.gameObject);
    }

    private static string Label(Car car) => car.CarType.IsTender ? car.Ident.RoadNumber : $"<line-height=70%>{car.Ident.ReportingMark}\n{car.Ident.RoadNumber}";

    private static void Click(Car car) => ClickCar(car);
  }
}
