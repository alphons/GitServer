'use strict';
(function () {
	async function streamInto(el, res) {
		var streamer = new MarkdownStreamer(el);

		if (res.body && res.body.getReader) {
			var reader = res.body.getReader();
			var decoder = new TextDecoder();
			for (;;) {
				var chunk = await reader.read();
				if (chunk.done) break;
				streamer.markdown(decoder.decode(chunk.value, { stream: true }));
			}
			streamer.markdown(decoder.decode());
		} else {
			// No streaming body support (very old browser) — fall back to buffering the whole response.
			streamer.markdown(await res.text());
		}

		// A trailing block (e.g. a raw HTML block) right at end-of-input can stay stuck in the
		// parser's pending buffer and never get flushed to the DOM. Feeding a couple of trailing
		// newlines guarantees something follows the last block so it flushes.
		streamer.markdown('\n\n');

		if (window.hljs) {
			el.querySelectorAll('pre code').forEach(function (block) { hljs.highlightElement(block); });
		}
	}

	function renderAll() {
		document.querySelectorAll('[data-markdown-src]').forEach(function (el) {
			var src = el.getAttribute('data-markdown-src');
			el.removeAttribute('data-markdown-src');
			fetch(src)
				.then(function (res) { return res.ok ? streamInto(el, res) : Promise.reject(res.status); })
				.catch(function () { /* source unavailable — leave the container empty */ });
		});
	}

	if (document.readyState === 'loading') {
		document.addEventListener('DOMContentLoaded', renderAll);
	} else {
		renderAll();
	}
})();
