document.addEventListener('DOMContentLoaded', function () {
	var apiUrl = '/api/admin/audit';
	var labels = JSON.parse(document.getElementById('audit-labels').textContent);
	var listEl = document.getElementById('audit-list');
	var filterEl = document.getElementById('audit-filter');
	var exportEl = document.getElementById('audit-export');
	var timer = null;
	var seq = 0;

	function cell(text, className) {
		var td = document.createElement('td');
		if (className) td.className = className;
		td.textContent = text == null ? '' : text;
		return td;
	}

	function pager(data, filter) {
		var box = document.createElement('div');
		box.className = 'pagination';
		[[labels.prev, data.page > 0, data.page - 1], [labels.next, data.hasNext, data.page + 1]].forEach(function (b) {
			if (!b[1]) return;
			var btn = document.createElement('button');
			btn.type = 'button';
			btn.className = 'btn btn-secondary';
			btn.textContent = b[0];
			btn.addEventListener('click', function () { load(filter, b[2]); });
			box.appendChild(btn);
		});
		return box;
	}

	function render(data, filter) {
		listEl.textContent = '';
		if (!data.entries.length) {
			var none = document.createElement('p');
			none.className = 'text-muted';
			none.textContent = labels.empty;
			listEl.appendChild(none);
			return;
		}

		var table = document.createElement('table');
		table.className = 'data-table';
		var head = document.createElement('tr');
		[labels.time, labels.user, labels.action, labels.target, labels.details, labels.ip].forEach(function (h) {
			var th = document.createElement('th');
			th.textContent = h;
			head.appendChild(th);
		});
		var thead = document.createElement('thead');
		thead.appendChild(head);
		table.appendChild(thead);

		var tbody = document.createElement('tbody');
		data.entries.forEach(function (e) {
			var tr = document.createElement('tr');
			tr.appendChild(cell(e.at, 'text-muted'));
			tr.appendChild(cell(e.actor + (e.via === 'api-key' ? ' (API)' : '')));
			var action = cell();
			var code = document.createElement('code');
			code.textContent = e.action;
			action.appendChild(code);
			tr.appendChild(action);
			tr.appendChild(cell(e.target));
			tr.appendChild(cell(e.details, 'text-muted'));
			tr.appendChild(cell(e.ip, 'text-muted'));
			tbody.appendChild(tr);
		});
		table.appendChild(tbody);
		listEl.appendChild(table);
		listEl.appendChild(pager(data, filter));
	}

	function load(filter, page) {
		var mine = ++seq;
		exportEl.href = apiUrl + '/export?q=' + encodeURIComponent(filter);
		fetch(apiUrl + '?q=' + encodeURIComponent(filter) + '&p=' + page)
			.then(function (r) { return r.json(); })
			.then(function (data) { if (mine === seq) render(data, filter); });
	}

	filterEl.addEventListener('input', function () {
		clearTimeout(timer);
		timer = setTimeout(function () { load(filterEl.value, 0); }, 250);
	});

	load('', 0);
});
