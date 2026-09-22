document.addEventListener('DOMContentLoaded', function () {
	var backdrop = document.getElementById('user-modal-backdrop');
	var form = document.getElementById('user-form');
	var title = document.getElementById('user-modal-title');
	var fUserId = document.getElementById('f-userId');
	var fQ = document.getElementById('f-q');
	var fP = document.getElementById('f-p');
	var fUserName = document.getElementById('f-userName');
	var fDisplayName = document.getElementById('f-displayName');
	var fEmail = document.getElementById('f-email');
	var fIsDisabled = document.getElementById('f-isDisabled');
	var fIsAdmin = document.getElementById('f-isAdmin');
	var fNewPassword = document.getElementById('f-newPassword');
	var fConfirmPassword = document.getElementById('f-confirmPassword');
	var fPendingHint = document.getElementById('f-pending-hint');
	var fStatusGroup = document.getElementById('f-status-group');
	var deleteBtn = document.getElementById('user-delete-btn');
	var newUserBtn = document.getElementById('new-user-btn');
	var closeBtn = document.getElementById('user-modal-close');
	var cancelBtn = document.getElementById('user-cancel-btn');
	var deleteForm = document.getElementById('user-delete-form');
	var deleteUserId = document.getElementById('delete-userId');
	var deleteQ = document.getElementById('delete-q');
	var deleteP = document.getElementById('delete-p');
	var confirmBackdrop = document.getElementById('confirm-modal-backdrop');
	var confirmText = document.getElementById('confirm-modal-text');
	var confirmOkBtn = document.getElementById('confirm-modal-ok');
	var confirmCancelBtn = document.getElementById('confirm-modal-cancel');
	var filterInput = document.getElementById('users-filter');
	var listContainer = document.getElementById('users-list-container');

	var strings = JSON.parse(document.getElementById('admin-users-strings').textContent);
	var editTitle = strings.editTitle;
	var newTitle = strings.newTitle;
	var confirmDeleteTemplate = strings.confirmDeleteTemplate;
	var labels = strings.labels;

	var currentFilter = filterInput.value;
	var currentPage = parseInt(filterInput.dataset.page, 10) || 0;
	var loadSeq = 0;

	function cell(row, text) {
		var td = document.createElement('td');
		td.textContent = text;
		row.appendChild(td);
		return td;
	}

	function badge(className, text, style) {
		var s = document.createElement('span');
		s.className = 'badge ' + className;
		s.textContent = text;
		if (style) s.setAttribute('style', style);
		return s;
	}

	function renderUsers(data) {
		listContainer.textContent = '';

		var table = document.createElement('table');
		table.className = 'data-table';
		var headRow = document.createElement('tr');
		[labels.user, labels.displayName, labels.email, labels.created, labels.lastLogin, labels.status, labels.admin]
			.forEach(function (h) {
				var th = document.createElement('th');
				th.textContent = h;
				headRow.appendChild(th);
			});
		var thead = document.createElement('thead');
		thead.appendChild(headRow);
		table.appendChild(thead);

		var tbody = document.createElement('tbody');
		data.users.forEach(function (u) {
			var tr = document.createElement('tr');
			tr.className = 'user-row';
			tr.style.cursor = 'pointer';
			tr.dataset.userId = u.id;
			tr.dataset.username = u.userName;
			tr.dataset.displayName = u.displayName;
			tr.dataset.email = u.email;
			tr.dataset.isDisabled = u.isDisabled ? 'true' : 'false';
			tr.dataset.isAdmin = u.isAdmin ? 'true' : 'false';
			tr.dataset.isPending = u.isPending ? 'true' : 'false';
			tr.dataset.isSelf = u.isSelf ? 'true' : 'false';

			var nameCell = cell(tr, u.isPending ? '' : u.userName);
			if (u.isPending) nameCell.appendChild(badge('badge-read', labels.pending));
			cell(tr, u.displayName);
			cell(tr, u.email);
			cell(tr, u.created);
			cell(tr, u.lastLogin || labels.never);
			var statusCell = cell(tr, '');
			statusCell.appendChild(u.isDisabled
				? badge('badge-write', labels.disabled, 'background:rgba(248,81,73,0.15);color:var(--danger)')
				: badge('badge-write', labels.enabled));
			cell(tr, u.isAdmin ? '✔' : '');
			tbody.appendChild(tr);
		});
		if (!data.users.length) {
			var empty = document.createElement('tr');
			var td = cell(empty, labels.noResults);
			td.colSpan = 7;
			td.className = 'text-muted';
			tbody.appendChild(empty);
		}
		table.appendChild(tbody);
		listContainer.appendChild(table);

		var pager = document.createElement('div');
		pager.className = 'pagination';
		pager.id = 'users-pagination';
		var prev = document.createElement('button');
		prev.type = 'button';
		prev.className = 'btn btn-secondary';
		prev.id = 'users-prev-btn';
		prev.textContent = labels.prev;
		prev.disabled = data.page <= 0;
		var info = document.createElement('span');
		info.className = 'text-muted';
		info.textContent = labels.pageOf.replace('{0}', data.page + 1).replace('{1}', data.totalPages);
		var next = document.createElement('button');
		next.type = 'button';
		next.className = 'btn btn-secondary';
		next.id = 'users-next-btn';
		next.textContent = labels.next;
		next.disabled = !data.hasNext;
		pager.appendChild(prev);
		pager.appendChild(info);
		pager.appendChild(next);
		listContainer.appendChild(pager);
	}

	function openConfirm(message, onConfirm) {
		confirmText.textContent = message;
		confirmOkBtn.onclick = function () {
			confirmBackdrop.hidden = true;
			onConfirm();
		};
		confirmBackdrop.hidden = false;
	}

	confirmCancelBtn.addEventListener('click', function () { confirmBackdrop.hidden = true; });
	confirmBackdrop.addEventListener('click', function (e) {
		if (e.target === confirmBackdrop) confirmBackdrop.hidden = true;
	});

	function openModal() { backdrop.hidden = false; }
	function closeModal() { backdrop.hidden = true; }

	function resetForm() {
		form.reset();
		fUserId.value = '';
		fNewPassword.value = '';
		fConfirmPassword.value = '';
		fQ.value = currentFilter;
		fP.value = currentPage;
	}

	newUserBtn.addEventListener('click', function () {
		resetForm();
		title.textContent = newTitle;
		fPendingHint.hidden = true;
		fStatusGroup.hidden = true;
		fIsDisabled.value = 'false';
		deleteBtn.hidden = true;
		fUserName.readOnly = false;
		openModal();
	});

	function bindRow(row) {
		var isPending = row.dataset.isPending === 'true';
		var isSelf = row.dataset.isSelf === 'true';

		fUserId.value = row.dataset.userId;
		fUserName.value = row.dataset.username.indexOf('pending-') === 0 ? '' : row.dataset.username;
		fDisplayName.value = row.dataset.displayName;
		fEmail.value = row.dataset.email;
		fIsDisabled.value = row.dataset.isDisabled;
		fIsAdmin.checked = row.dataset.isAdmin === 'true';

		title.textContent = isPending ? newTitle : editTitle;
		fPendingHint.hidden = !isPending;
		fStatusGroup.hidden = isSelf;
		deleteBtn.hidden = isSelf;

		deleteBtn.onclick = function () {
			var message = confirmDeleteTemplate.replace('{0}', isPending ? row.dataset.email : row.dataset.username);
			openConfirm(message, function () {
				deleteUserId.value = row.dataset.userId;
				deleteQ.value = currentFilter;
				deleteP.value = currentPage;
				deleteForm.submit();
			});
		};
	}

	listContainer.addEventListener('click', function (e) {
		var row = e.target.closest('.user-row');
		if (row) {
			resetForm();
			bindRow(row);
			openModal();
			return;
		}

		if (e.target.id === 'users-prev-btn' && !e.target.disabled) {
			currentPage = Math.max(currentPage - 1, 0);
			loadUsers();
		} else if (e.target.id === 'users-next-btn' && !e.target.disabled) {
			currentPage = currentPage + 1;
			loadUsers();
		}
	});

	function loadUsers() {
		var url = '/api/admin/users?q=' + encodeURIComponent(currentFilter) + '&p=' + currentPage;
		var mine = ++loadSeq;
		fetch(url)
			.then(function (r) { return r.json(); })
			.then(function (data) { if (mine === loadSeq) renderUsers(data); });
	}

	loadUsers();

	filterInput.addEventListener('input', function () {
		currentFilter = filterInput.value;
		currentPage = 0;
		loadUsers();
	});

	closeBtn.addEventListener('click', closeModal);
	cancelBtn.addEventListener('click', closeModal);
	backdrop.addEventListener('click', function (e) {
		if (e.target === backdrop) closeModal();
	});
});
