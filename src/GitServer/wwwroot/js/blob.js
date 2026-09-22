document.addEventListener('DOMContentLoaded', function () {
	hljs.highlightAll();

	document.getElementById('copyRawBtn')?.addEventListener('click', async function () {
		const code = document.querySelector('.code-view code');
		if (!code) return;
		await navigator.clipboard.writeText(code.innerText);
		const original = this.title;
		this.title = 'Copied!';
		setTimeout(() => { this.title = original; }, 1500);
	});
});
