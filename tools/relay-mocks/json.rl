export let files = {}

export func load(path) {
  if !files.has(path) {
    throw "File not found: \(path)"
  }
  return files[path].deepCopy()
}
