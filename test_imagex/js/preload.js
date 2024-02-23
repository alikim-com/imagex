const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('electronAPI', {
   onFilePath: (callback) => ipcRenderer.on('fileOpenPath',
      (_event, obj) => { callback(obj) }),
});

window.addEventListener('DOMContentLoaded', () => {
   
   const replaceText = (selector, text) => {
      const element = document.getElementById(selector);
      if (element) element.innerText = text;
   }
 
   for (const dependency of ['chrome', 'node', 'electron']) {
      replaceText(`${dependency}-version`, process.versions[dependency]);
   }

});