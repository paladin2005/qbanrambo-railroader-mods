export let calls = []
export let extensions = []
export let missingSegments = []

class TrackLocation {
  init(text) {
    var parts = text.split("|")
    if parts.count != 3 {
      throw "Location: Invalid string '\(text)'"
    }
    if missingSegments.contains(parts[0]) {
      throw "Location: segment not found: \(parts[0])"
    }
    self.text = text
    self.segmentId = parts[0]
  }

  func asString() {
    return self.text
  }
}

class Track {
  init() {
    self.distances = {}
  }

  func getDistance(a, b) {
    return self.distances["\(a.text)>\(b.text)"] ?? 100
  }
}

class Ends {
  var F { "F" }
  var R { "R" }
  var A { "A" }
  var B { "B" }
}

class Positions {
  var center { "center" }
  var centerRight { "centerRight" }
  var lowerLeft { "lowerLeft" }
  var lowerRight { "lowerRight" }
  var upperLeft { "upperLeft" }
  var upperRight { "upperRight" }
}

class Load {
  init(id, name) {
    self.id = id
    self.name = name
  }
}

class LoadSlot {
  init(load, quantity) {
    self.load = load
    self.quantity = quantity
  }
}

class Waybill {
  init(destination, payment, completed) {
    self.destination = destination
    self.paymentOnArrival = payment
    self.completed = completed
    self.origin = null
    self.tag = null
    self.graceDays = 0
  }
}

class Car {
  init(id, name, carType) {
    self.id = id
    self.name = name
    self.carType = carType
    self.length = 12.0
    self.speedMph = 0
    self.waybill = null
    self.loadInfo = []
    self.properties = {}
    self.isLocomotive = carType.startsWith("L") && !carType.startsWith("LT")
    self.fuelCar = self
    self.levels = {}
    self.failLoads = false
    self.consist = null
    self.locationFront = TrackLocation("\(id)-front|a|0")
    self.aeWaypointNotice = null
    self.failWaypoint = false
  }

  func setControlAeWaypoint(location, speed, coupleCarId = null) {
    if self.failWaypoint {
      throw "No route to waypoint"
    }
    calls.add("waypoint \(self.name) -> \(location.text) at \(speed)")
  }

  func getCoupledCars(end) {
    var consist = self.consist ?? [self]
    if end == "R" || end == "r" || end == "B" {
      return consist.reversed()
    }
    return consist.copy()
  }

  func getLoadPercent(loadId) {
    if self.failLoads {
      throw "Load \(loadId) can't be applied to \(self.name)"
    }
    return self.levels[loadId] ?? 0
  }

  func select() {
    calls.add("select \(self.name)")
  }

  func follow() {
    calls.add("follow \(self.name)")
  }
}

class Industry {
  init(id, name) {
    self.identifier = id
    self.name = name
    self.components = []
    self.cars = []
  }
}

class IndustryComponent {
  init(id, name, industry) {
    self.identifier = id
    self.subIdentifier = id
    self.name = name
    self.industry = industry
  }
}

class Ops {
  init() {
    self.industries = {}
    self.industryComponents = {}
    self.interchangeCars = []
  }

  func getAllIndustryIds() {
    return self.industries.keys()
  }

  func getIndustry(id) {
    if !self.industries.has(id) {
      throw "Industry not found: \(id)"
    }
    return self.industries[id]
  }

  func getIndustryComponent(id) {
    if !self.industryComponents.has(id) {
      throw "Industry component not found: \(id)"
    }
    return self.industryComponents[id]
  }

  func getCarsAtIndustry(id) {
    return self.getIndustry(id).cars
  }

  func getCarsAtInterchange() {
    return self.interchangeCars
  }
}

class World {
  init() {
    self.cars = []
    self.ops = Ops()
    self.track = Track()
    self.frameTime = 0.0
    self.isHost = true
  }

  func getCars(filter) {
    return self.cars
  }

  func jumpToIndustry(id) {
    calls.add("jumpToIndustry \(id)")
  }

  func jumpToPosition(position) {
    calls.add("jumpToPosition \(position.x) \(position.y) \(position.z)")
  }
}

class Element {
  init(node) {
    self.node = node
  }

  func width(value) {
    self.node["width"] = value
    return self
  }

  func height(value) {
    return self
  }

  func disable(value) {
    self.node["disabled"] = value
    return self
  }

  func tooltip(title, message) {
    return self
  }
}

class Builder {
  init(nodes) {
    self.nodes = nodes
  }

  func add(kind, text) {
    var node = {"kind": kind, "text": text, "children": [], "disabled": false, "action": null}
    self.nodes.add(node)
    return node
  }

  func container(kind, text, build) {
    var node = self.add(kind, text)
    if build != null {
      build(Builder(node["children"]))
    }
  }

  func hStack(build) {
    self.container("hStack", "", build)
  }

  func vStack(build) {
    self.container("vStack", "", build)
  }

  func vScrollView(build) {
    self.container("scroll", "", build)
  }

  func section(title, build = null) {
    self.container("section", title, build)
  }

  func label(text) {
    return Element(self.add("label", text))
  }

  func labelMarkup(markup) {
    self.add("markup", markup)
  }

  func labelEmptyState(text) {
    self.add("empty", text)
  }

  func title(text, subtitle = null) {
    self.add("title", text)
  }

  func field(label, value) {
    return Element(self.add("field", "\(label): \(value)"))
  }

  func button(label, onClick) {
    var node = self.add("button", label)
    node["action"] = onClick
    return Element(node)
  }

  func fieldDropdown(label, options, currentIndex, onChanged) {
    var node = self.add("dropdown", "\(label): \(options[currentIndex])")
    node["action"] = onChanged
    return Element(node)
  }

  func dropdown(options, currentIndex, onChanged) {
    return self.fieldDropdown("Dropdown", options, currentIndex, onChanged)
  }

  func fieldToggle(label, getValue, onChanged) {
    var node = self.add("toggle", "\(label): \(getValue())")
    node["action"] = onChanged
    return Element(node)
  }

  func spacer(size) {
    self.add("spacer", "")
  }

  func expandingVerticalSpacer() {
  }

  func configureLayout(spacing, padding = null, childAlignment = null) {
  }

  func rebuildOnPropertyChange(properties, key) {
  }

  func addObserver(disposable) {
  }

  func setNeedsRebuild() {
    calls.add("panel rebuild")
  }

  func rebuildImmediately() {
    calls.add("panel rebuild")
  }
}

class Window {
  init(identifier, title, build) {
    self.identifier = identifier
    self.title = title
    self.build = build
    self.open = false
    self.width = 400
    self.height = 300
  }

  func size(width, height) {
    self.width = width
    self.height = height
    return self
  }

  func position(value) {
    return self
  }

  func resizable(minWidth, minHeight, maxWidth, maxHeight) {
    return self
  }

  func fixedSize() {
    return self
  }

  func show() {
    self.open = true
  }

  func close() {
    self.open = false
  }

  func toggle() {
    self.open = !self.open
  }

  func isOpen() {
    return self.open
  }

  func setNeedsRebuild() {
  }

  func rebuildImmediately() {
  }

  func setTitle(title) {
    self.title = title
  }

  func render() {
    var nodes = []
    var build = self.build
    build(Builder(nodes))
    return nodes
  }
}

class Extension {
  init(point) {
    self.point = point
    self.identifier = null
    self.criteria = []
    self.order = 0
    self.build = null
  }

  func id(value) {
    self.identifier = value
    return self
  }

  func matching(criteria) {
    self.criteria.add(criteria)
    return self
  }

  func priority(value) {
    self.order = value
    return self
  }

  func with(build) {
    self.build = build
    extensions.add(self)
  }

  func render(car) {
    var nodes = []
    var build = self.build
    build(Builder(nodes), car)
    return nodes
  }
}

class UI {
  init() {
    self.windows = {}
    self.commands = {}
  }

  func createWindow(identifier, title, build) {
    var window = Window(identifier, title, build)
    self.windows[identifier] = window
    return window
  }

  func getWindow(identifier) {
    return self.windows[identifier]
  }

  func registerCommand(commandName, description, callback) {
    var key = commandName.startsWith("/") ? commandName : "/\(commandName)"
    self.commands[key] = callback
  }

  func extend(extensionPoint) {
    return Extension(extensionPoint)
  }

  func registerStyleProperty(carId, selector, property, label, editor) {
  }
}

export let world = World()
export let ui = UI()
export let CarEnd = Ends()
export let WindowPosition = Positions()

export func Location(text) {
  return TrackLocation(text)
}

export func mockCar(id, name, carType) {
  return Car(id, name, carType)
}

export func mockLoad(id, name) {
  return Load(id, name)
}

export func mockSlot(load, quantity) {
  return LoadSlot(load, quantity)
}

export func mockWaybill(destination, payment, completed) {
  return Waybill(destination, payment, completed)
}

export func mockIndustry(id, name) {
  var industry = Industry(id, name)
  world.ops.industries[id] = industry
  return industry
}

export func mockIndustryComponent(id, name, industry) {
  var component = IndustryComponent(id, name, industry)
  industry.components.add(component)
  world.ops.industryComponents[id] = component
  return component
}

export func couple(cars) {
  for car in cars {
    car.consist = cars
  }
}

export func render(nodes) {
  var lines = []
  renderInto(lines, nodes, "")
  return lines.join("\n")
}

func renderInto(lines, nodes, indent) {
  for node in nodes {
    var kind = node["kind"]
    if kind == "hStack" {
      var parts = node["children"].filter((child) => child["kind"] != "spacer").map((child) => describe(child))
      lines.add(indent + parts.join("  |  "))
    } else if kind != "spacer" {
      lines.add(indent + describe(node))
      renderInto(lines, node["children"], indent + "  ")
    }
  }
}

func describe(node) {
  var kind = node["kind"]
  var text = node["text"]
  if kind == "button" {
    return node["disabled"] ? "[(\(text))]" : "[\(text)]"
  }
  if kind == "dropdown" {
    return "<\(text)>"
  }
  if kind == "empty" {
    return "(\(text))"
  }
  if kind == "section" {
    return "## \(text)"
  }
  if kind == "scroll" {
    return "--- scroll"
  }
  return text
}

export func click(nodes, label) {
  clickNth(nodes, label, 0)
}

export func clickNth(nodes, label, index) {
  var matches = []
  findNodes(nodes, "button", label, matches)
  if matches.count <= index {
    throw "No button '\(label)' number \(index + 1)"
  }
  var node = matches[index]
  if node["disabled"] {
    throw "Button '\(label)' is disabled"
  }
  var action = node["action"]
  action()
}

export func choose(nodes, label, index) {
  var node = findNode(nodes, "dropdown", "\(label):")
  if node == null {
    throw "No dropdown '\(label)'"
  }
  var action = node["action"]
  action(index)
}

func findNodes(nodes, kind, text, matches) {
  for node in nodes {
    if node["kind"] == kind && node["text"] == text {
      matches.add(node)
    }
    findNodes(node["children"], kind, text, matches)
  }
}

func findNode(nodes, kind, text) {
  for node in nodes {
    if node["kind"] == kind && (node["text"] == text || (kind == "dropdown" && node["text"].startsWith(text))) {
      return node
    }
    var found = findNode(node["children"], kind, text)
    if found != null {
      return found
    }
  }
  return null
}
