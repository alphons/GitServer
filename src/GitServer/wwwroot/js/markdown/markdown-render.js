'use strict';
(function () {
	function renderAll() {
		document.querySelectorAll('script.markdown-source').forEach(function (scriptEl) {
			var target = scriptEl.nextElementSibling;
			var source = JSON.parse(scriptEl.textContent);
			scriptEl.remove();
			// A trailing block (e.g. a raw HTML block) right at end-of-input can stay stuck in
			// the streaming parser's pending buffer and never get flushed to the DOM. Padding
			// with a couple of newlines guarantees something follows the last block so it flushes.
			if (target) new MarkdownStreamer(target).markdown(source + '\n\n');
		});
	}

	if (document.readyState === 'loading') {
		document.addEventListener('DOMContentLoaded', renderAll);
	} else {
		renderAll();
	}
})();
