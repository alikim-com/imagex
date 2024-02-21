const getHTML = () => {
   const html = {};
   document.querySelectorAll('*[id]').forEach(e => html[e.id] = e);
   return html;
};

const html = getHTML();

html.btn_fileopen.addEventListener('click', async () => {
   const filePath = await window.electronAPI.openFile();
   html.info.innerText = filePath;
});

window.electronAPI.onFilePath((value) => {
   html.info.innerText = value;
   // resp
   //window.electronAPI.counterValue(newValue);
 });