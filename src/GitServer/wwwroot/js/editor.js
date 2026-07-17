// Simple Markdown toolbar voor issue editor
(function () {
  const textareas = document.querySelectorAll('.editor-textarea');

  textareas.forEach(function (textarea) {
    const toolbar = document.createElement('div');
    toolbar.className = 'editor-toolbar';

    const buttons = [
      { label: 'B', title: 'Vet', before: '**', after: '**' },
      { label: 'I', title: 'Cursief', before: '*', after: '*' },
      { label: '`', title: 'Code', before: '`', after: '`' },
      { label: '```', title: 'Codeblok', before: '```\n', after: '\n```' },
      { label: 'H2', title: 'Kop 2', before: '## ', after: '' },
      { label: '> ', title: 'Citaat', before: '> ', after: '' },
    ];

    buttons.forEach(function (def) {
      const btn = document.createElement('button');
      btn.type = 'button';
      btn.className = 'btn btn-sm btn-secondary editor-btn';
      btn.textContent = def.label;
      btn.title = def.title;

      btn.addEventListener('click', function () {
        const start = textarea.selectionStart;
        const end = textarea.selectionEnd;
        const selected = textarea.value.substring(start, end);
        const replacement = def.before + selected + def.after;

        textarea.focus();
        document.execCommand('insertText', false, replacement);

        if (selected.length === 0) {
          const cursorPos = start + def.before.length;
          textarea.setSelectionRange(cursorPos, cursorPos);
        }
      });

      toolbar.appendChild(btn);
    });

    textarea.parentNode.insertBefore(toolbar, textarea);
  });
})();
