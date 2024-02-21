const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('electronAPI', {
   setTitle: (title) => ipcRenderer.send('set-title', title),
   //openFile: () => ipcRenderer.invoke('dialog:openFile'),
   // resp
   //counterValue: (value) => ipcRenderer.send('counter-value', value),
   onFilePath: (callback) => ipcRenderer.on('fileOpenPath',
      (_event, value) => callback(value)),
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