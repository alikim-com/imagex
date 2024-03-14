function genEnum(table) {
    const rows = table.querySelectorAll('tr');
    let enumString = 'public enum TagEnum {\n';

    for (let i = 0; i < rows.length; i++) {
        const cells = rows[i].querySelectorAll('td');
        if (cells.length >= 2) {
            const value = cells[0].textContent.trim();
            const name = cells[1].textContent.trim();
            const line = `${name} = ${value},\n`;
            enumString += line;
        }
    }

    enumString += '}';
    return enumString;
}

genEnum($0);