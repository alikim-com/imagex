const {
   app, BrowserWindow, nativeTheme, Menu, dialog,
   ipcMain
} = require('electron');
const fs = require('node:fs');
const path = require('node:path');

const [winWidth, winHeight] = [1200, 800];

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
      width: winWidth,
      height: 1, // mitigate white flash
      webPreferences: {
         preload: path.join(__dirname, './js/preload.js')
      }
   });

   mainWindow.once('ready-to-show', () => {
      mainWindow.setSize(winWidth, winHeight);
      mainWindow.center();
    });

   const menu = Menu.buildFromTemplate([
      {
         label: 'File',
         submenu: [
            {
               label: 'Open...',
               accelerator: 'CmdOrCtrl+O',
               click: async () => {
                  const fPath = await handleFileDialog();
                  if (fPath == '') return;

                  const obj = {href: null, fdat: null};

                  // html img
                  const fullPath = `file://${pathToUri(fPath)}`;
                  const furl = new URL(fullPath);
                  obj.href = furl.href;

                  // byte array
                  const dPath = fPath + '.xdat';
                  console.log(`Reading data file '${dPath}'...`);
                  fs.readFile(dPath, (err, buff) => {
                     if (err) {
                        console.error('Error reading file:', err);
                        return;
                     }
                     obj.fdat = new Uint8ClampedArray(buff);

                     mainWindow.webContents.send('fileOpenPath', obj);
                  });                 
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
            },
            {
               label: 'Toggle DevTools',
               accelerator: 'CmdOrCtrl+Shift+I',
               click: () => { mainWindow.webContents.toggleDevTools() }
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

async function handleFileDialog() {
   const { canceled, filePaths } = await dialog.showOpenDialog({});
   if (!canceled) return filePaths[0];
   return '';
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