'use strict';
(function () {
	function renderAll() {
		document.querySelectorAll('[data-markdown-src]').forEach(function (el) {
			var src = el.getAttribute('data-markdown-src');
			el.removeAttribute('data-markdown-src');
			fetch(src)
				.then(function (res) { return res.ok ? res.text() : Promise.reject(res.status); })
				.then(function (text) {
					// A trailing block (e.g. a raw HTML block) right at end-of-input can stay
					// stuck in the streaming parser's pending buffer and never get flushed to
					// the DOM. Padding with a couple of newlines guarantees something follows
					// the last block so it flushes.
					new MarkdownStreamer(el).markdown(text + '\n\n');
				})
				.catch(function () { /* source unavailable — leave the container empty */ });
		});
	}

	if (document.readyState === 'loading') {
		document.addEventListener('DOMContentLoaded', renderAll);
	} else {
		renderAll();
	}
})();
