const getHTML = () => {
   const html = {};
   document.querySelectorAll('*[id]').forEach(e => html[e.id] = e);
   return html;
};

const html = getHTML();

html.btn_fileopen.addEventListener('click', async () => {
   const filePath = await window.electronAPI.openFile();
   info.innerText = filePath;
});