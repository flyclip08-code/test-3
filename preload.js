const { contextBridge, ipcRenderer } = require("electron");
contextBridge.exposeInMainWorld("fnBridge", {
  onCommand: (cb) => ipcRenderer.on("cmd", (_e, c) => cb(c)),
  sendState: (s) => ipcRenderer.send("state", s),
  onState: (cb) => ipcRenderer.on("state", (_e, s) => cb(s)),
  onMode: (cb) => ipcRenderer.on("mode", (_e, m) => cb(m)),
  getSettings: () => ipcRenderer.invoke("settings:get"),
  setKey: (id, key, on) => ipcRenderer.invoke("keys:set", id, key, on),
  ovCmd: (c, v) => ipcRenderer.send("ov:cmd", c, v),
  drag: (phase) => ipcRenderer.send("ov:drag", phase),
  scanInfo: () => ipcRenderer.invoke("scan:info"),
  scanAll: (names) => ipcRenderer.invoke("scan:all", names),
  setAuto: (v) => ipcRenderer.invoke("scan:auto", v),
  onScan: (cb) => ipcRenderer.on("scan", (_e, r) => cb(r)),
  onProgress: (cb) => ipcRenderer.on("scan:progress", (_e, p) => cb(p)),
});
