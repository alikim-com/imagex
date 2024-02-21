const { app, BrowserWindow, Menu, ipcMain, dialog } = require('electron');
const path = require('node:path');


const createWindow = () => {
   const mainWindow = new BrowserWindow({
      width: 1200,
      height: 800,
      webPreferences: {
         preload: path.join(__dirname, './js/preload.js')
      }
   });

   const menu = Menu.buildFromTemplate([
      {
         label: 'File',
         submenu: [
            {
               label: 'Open...',
               accelerator: 'CmdOrCtrl+O',
               click: async () => {
                  const path = await handleFileOpen();
                  mainWindow.webContents.send('fileOpenPath', path);
               }
            },
         ]
      },
      {
         label: 'Window',
         submenu: [
            {
               label: 'Reload page',
               accelerator: 'CmdOrCtrl+R',
               click: () => { mainWindow.webContents.reload() }
            }
         ]
      }
   ]);
   Menu.setApplicationMenu(menu);
   //const _menu = Menu.getApplicationMenu();
   //console.log(_menu);
   mainWindow.webContents.openDevTools();

   // resp
   ipcMain.on('counter-value', (_event, value) => {
      console.log(value)
   });

   mainWindow.loadFile('index.html');
};

function handleSetTitle(event, title) {
   const webContents = event.sender;
   const win = BrowserWindow.fromWebContents(webContents);
   win.setTitle(title);
}

async function handleFileOpen() {
   const { canceled, filePaths } = await dialog.showOpenDialog({});
   if (!canceled) return filePaths[0];
   return [];
}

app.on('window-all-closed', () => {
   if (process.platform !== 'darwin') app.quit()
});

app.whenReady().then(() => {

   // renderer to main (one-way)
   ipcMain.on('set-title', handleSetTitle);

   // renderer to main (two-way)
   // ipcMain.handle('dialog:openFile', handleFileOpen);

   createWindow();

   app.on('activate', () => {
      if (BrowserWindow.getAllWindows().length === 0) createWindow()
   });

});