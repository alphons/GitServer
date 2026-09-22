document.addEventListener('DOMContentLoaded', function () {
	var apiUrl = '/api/user/api-keys';
	var labels = JSON.parse(document.getElementById('apikeys-labels').textContent);
	var token = document.querySelector('#apikeys-antiforgery input[name="__RequestVerificationToken"]').value;
	var listEl = document.getElementById('apikeys-list');
	var errorEl = document.getElementById('apikeys-error');
	var newBox = document.getElementById('apikeys-new');
	var newValue = document.getElementById('apikeys-new-value');
	var form = document.getElementById('apikeys-form');
	var nameInput = document.getElementById('apikeys-name');
	var readOnlyInput = document.getElementById('apikeys-readonly');

	function showError(message) {
		errorEl.hidden = !message;
		errorEl.textContent = message || '';
	}

	function send(method, url, body) {
		showError('');
		return fetch(url, {
			method: method,
			headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
			body: body ? JSON.stringify(body) : undefined,
		}).then(function (r) {
			if (r.ok) return r.status === 204 ? null : r.json();
			return r.json().catch(function () { return {}; }).then(function (e) {
				showError(e.error || labels.failed);
				throw new Error('rejected');
			});
		});
	}

	function cell(text, className) {
		var td = document.createElement('td');
		if (className) td.className = className;
		if (text != null) td.textContent = text;
		return td;
	}

	function button(text, className, onClick) {
		var b = document.createElement('button');
		b.type = 'button';
		b.className = 'btn btn-sm ' + className;
		b.textContent = text;
		b.addEventListener('click', onClick);
		return b;
	}

	function render(keys) {
		listEl.textContent = '';
		if (!keys.length) {
			var none = document.createElement('p');
			none.className = 'text-muted';
			none.textContent = labels.none;
			listEl.appendChild(none);
			return;
		}

		var table = document.createElement('table');
		table.className = 'data-table';
		var headRow = document.createElement('tr');
		[labels.name, labels.status, labels.created, labels.lastUsed, labels.expires, ''].forEach(function (h) {
			var th = document.createElement('th');
			th.textContent = h;
			headRow.appendChild(th);
		});
		var thead = document.createElement('thead');
		thead.appendChild(headRow);
		table.appendChild(thead);
		var tbody = document.createElement('tbody');
		keys.forEach(function (k) {
			var tr = document.createElement('tr');

			var nameTd = cell(k.name);
			var prefix = document.createElement('code');
			prefix.textContent = ' ' + k.prefix + '…';
			nameTd.appendChild(prefix);
			if (k.isReadOnly) {
				var ro = document.createElement('span');
				ro.className = 'badge badge-read';
				ro.textContent = labels.readOnly;
				nameTd.appendChild(document.createTextNode(' '));
				nameTd.appendChild(ro);
			}
			tr.appendChild(nameTd);

			var status = document.createElement('span');
			status.className = 'badge ' + (k.isExpired || !k.isEnabled ? 'badge-warning' : 'badge-success');
			status.textContent = k.isExpired ? labels.expired : (k.isEnabled ? labels.enabled : labels.disabled);
			var statusTd = cell();
			statusTd.appendChild(status);
			tr.appendChild(statusTd);

			tr.appendChild(cell(k.created, 'text-muted'));
			tr.appendChild(cell(k.lastUsed || labels.never, 'text-muted'));
			tr.appendChild(cell(k.expires, 'text-muted'));

			var actions = cell();
			actions.appendChild(button(k.isEnabled ? labels.disable : labels.enable, 'btn-secondary', function () {
				send('POST', apiUrl + '/' + k.id + '/enabled', { enabled: !k.isEnabled }).then(load).catch(function () { });
			}));
			actions.appendChild(document.createTextNode(' '));
			actions.appendChild(button(labels.delete, 'btn-danger', function () {
				if (!confirm(labels.confirmDelete)) return;
				send('POST', apiUrl + '/' + k.id + '/delete').then(load).catch(function () { });
			}));
			tr.appendChild(actions);

			tbody.appendChild(tr);
		});
		table.appendChild(tbody);
		listEl.appendChild(table);
	}

	function load() {
		fetch(apiUrl).then(function (r) { return r.json(); }).then(render);
	}

	form.addEventListener('submit', function (e) {
		e.preventDefault();
		send('POST', apiUrl, { name: nameInput.value, readOnly: readOnlyInput.checked })
			.then(function (data) {
				nameInput.value = '';
				newValue.textContent = data.key;
				newBox.hidden = false;
				load();
			})
			.catch(function () { });
	});

	load();
});
