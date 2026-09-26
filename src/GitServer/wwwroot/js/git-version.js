var apiBase = '/api/admin/git';

document.addEventListener('DOMContentLoaded', function () {
	(function () {
		var installBtn = document.getElementById('install-btn');
		if (!installBtn) return;

		var progressBox = document.getElementById('install-progress');
		var progressBar = document.getElementById('install-progress-bar');
		var progressText = document.getElementById('install-progress-text');
		var errorBox = document.getElementById('install-progress-error');
		var token = document.querySelector('#antiforgery-form input[name="__RequestVerificationToken"]').value;

		function formatBytes(bytes) {
			if (!bytes) return '0 MB';
			return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
		}

		function poll(jobId) {
			fetch(apiBase + '/installs/' + encodeURIComponent(jobId))
				.then(function (r) { return r.json(); })
				.then(function (p) {
					if (p.totalBytes > 0) {
						var pct = Math.round((p.bytesDownloaded / p.totalBytes) * 100);
						progressBar.value = pct;
						progressText.textContent = pct + '% (' + formatBytes(p.bytesDownloaded) + ' / ' + formatBytes(p.totalBytes) + ')';
					} else {
						progressText.textContent = formatBytes(p.bytesDownloaded);
					}

					if (p.completed) {
						if (p.failed) {
							progressBox.hidden = true;
							errorBox.hidden = false;
							errorBox.textContent = p.error || installBtn.dataset.errorFailed;
							installBtn.disabled = false;
						} else {
							progressText.textContent = '100%';
							window.location.reload();
						}
						return;
					}

					setTimeout(function () { poll(jobId); }, 500);
				})
				.catch(function () {
					setTimeout(function () { poll(jobId); }, 1000);
				});
		}

		installBtn.addEventListener('click', function () {
			installBtn.disabled = true;
			errorBox.hidden = true;
			progressBox.hidden = false;
			progressBar.value = 0;
			progressText.textContent = '';

			fetch(apiBase + '/installs', {
				method: 'POST',
				headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
				body: JSON.stringify({ tagName: installBtn.dataset.tagName }),
			})
				.then(function (r) { return r.json(); })
				.then(function (data) { poll(data.jobId); })
				.catch(function () {
					progressBox.hidden = true;
					errorBox.hidden = false;
					errorBox.textContent = installBtn.dataset.errorStart;
					installBtn.disabled = false;
				});
		});
	})();

	(function () {
		var browseBtns = document.querySelectorAll('.browse-btn');
		if (!browseBtns.length) return;

		var modal = document.getElementById('browse-modal');
		var closeBtn = document.getElementById('browse-modal-close');
		var breadcrumb = document.getElementById('browse-breadcrumb');
		var listEl = document.getElementById('browse-list');
		var fileContentEl = document.getElementById('browse-file-content');
		var backBtn = document.getElementById('browse-back-btn');
		var currentInstallationId = null;

		function formatSize(bytes) {
			if (bytes == null) return '';
			if (bytes < 1024) return bytes + ' B';
			if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
			return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
		}

		function showList(path) {
			fileContentEl.hidden = true;
			backBtn.hidden = true;
			listEl.hidden = false;
			breadcrumb.textContent = '/' + path;

			fetch(apiBase + '/installations/' + currentInstallationId + '/entries?path=' + encodeURIComponent(path))
				.then(function (r) { return r.json(); })
				.then(function (entries) {
					listEl.innerHTML = '';

					if (path) {
						var up = document.createElement('li');
						up.className = 'is-dir';
						up.textContent = '..';
						up.addEventListener('click', function () {
							var parent = path.split('/').slice(0, -1).join('/');
							showList(parent);
						});
						listEl.appendChild(up);
					}

					entries.forEach(function (entry) {
						var li = document.createElement('li');
						var entryPath = path ? path + '/' + entry.name : entry.name;
						if (entry.isDirectory) {
							li.className = 'is-dir';
							li.textContent = entry.name;
							li.addEventListener('click', function () { showList(entryPath); });
						} else {
							li.className = 'is-file';
							li.textContent = entry.name + (entry.size != null ? ' (' + formatSize(entry.size) + ')' : '');
							li.addEventListener('click', function () { showFile(entryPath); });
						}
						listEl.appendChild(li);
					});
				});
		}

		function showFile(path) {
			breadcrumb.textContent = '/' + path;
			fetch(apiBase + '/installations/' + currentInstallationId + '/file?path=' + encodeURIComponent(path))
				.then(function (r) { return r.json(); })
				.then(function (data) {
					listEl.hidden = true;
					fileContentEl.hidden = false;
					fileContentEl.textContent = data.error || data.content;

					backBtn.hidden = false;
					backBtn.onclick = function () {
						showList(path.split('/').slice(0, -1).join('/'));
					};
				});
		}

		browseBtns.forEach(function (btn) {
			btn.addEventListener('click', function () {
				currentInstallationId = btn.dataset.installationId;
				modal.hidden = false;
				showList('');
			});
		});

		closeBtn.addEventListener('click', function () { modal.hidden = true; });
		modal.addEventListener('click', function (e) {
			if (e.target === modal) modal.hidden = true;
		});
	})();
});
