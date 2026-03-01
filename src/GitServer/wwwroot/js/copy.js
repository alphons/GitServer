function copyCloneUrl() {
  const input = document.getElementById('cloneUrl');
  if (!input) return;

  if (navigator.clipboard) {
    navigator.clipboard.writeText(input.value).then(function () {
      showCopied();
    }).catch(function () {
      fallbackCopy(input);
    });
  } else {
    fallbackCopy(input);
  }
}

function fallbackCopy(input) {
  input.select();
  input.setSelectionRange(0, 99999);
  try {
    document.execCommand('copy');
    showCopied();
  } catch (e) { }
}

function showCopied() {
  const btn = document.querySelector('.clone-url-box .btn');
  if (!btn) return;
  const orig = btn.textContent;
  btn.textContent = 'Gekopieerd!';
  btn.style.background = 'var(--success)';
  btn.style.borderColor = 'var(--success)';
  btn.style.color = '#fff';
  setTimeout(function () {
    btn.textContent = orig;
    btn.style.background = '';
    btn.style.borderColor = '';
    btn.style.color = '';
  }, 2000);
}
