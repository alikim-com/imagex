const { app, BrowserWindow, nativeTheme, Menu, ipcMain, dialog } = require('electron');
const path = require('node:path');

const pathToUri = path => {
   // no encodeURI() - it replaces slashes
   let outp = '';
   const repl = ['#'];
   for (const ch of path)
      outp += repl.includes(ch) ? '%' + ch.charCodeAt(0).toString(16) : ch;
   return outp;
};

const createWindow = () => {
   const mainWindow = new BrowserWindow({
      width: 1200,
      height: 800,
      transparent: true,
      webPreferences: {
         preload: path.join(__dirname, './js/preload.js')
      }
   });

   mainWindow.setOpacity(0.1);

   mainWindow.once('ready-to-show', () => {
      mainWindow.setOpacity(1);
    });

   const menu = Menu.buildFromTemplate([
      {
         label: 'File',
         submenu: [
            {
               label: 'Open...',
               accelerator: 'CmdOrCtrl+O',
               click: async () => {
                  const fpath = await handleFileOpen();
                  const fullPath = `file://${pathToUri(fpath)}`;
                  const furl = new URL(fullPath);
                  mainWindow.webContents.send('fileOpenPath', furl.href);
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
      },
      {
         label: 'Theme',
         submenu: [
            {
               label: 'Dark',
               click: () => { nativeTheme.themeSource = 'dark'}
            },
            {
               label: 'Light',
               click: () => { nativeTheme.themeSource = 'light'}
            },
            {
               label: 'System',
               click: () => { nativeTheme.themeSource = 'system'}
            }
         ]
      }
   ]);
   
   Menu.setApplicationMenu(menu);
   
   mainWindow.webContents.openDevTools();

   mainWindow.loadFile('index.html');
};

async function handleFileOpen() {
   const { canceled, filePaths } = await dialog.showOpenDialog({});
   if (!canceled) return filePaths[0];
   return [];
}

app.on('window-all-closed', () => {
   if (process.platform !== 'darwin') app.quit()
});

nativeTheme.themeSource = 'dark';

app.whenReady().then(() => {

   createWindow();

   app.on('activate', () => {
      if (BrowserWindow.getAllWindows().length === 0) createWindow()
   });

});