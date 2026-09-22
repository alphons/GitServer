document.addEventListener('DOMContentLoaded', function () {
	var memberInput = document.getElementById('memberName');
	if (memberInput) initCollaboratorSearch('memberName', 'memberResults', '/api/groups/' + memberInput.dataset.groupId + '/user-search');

	(function () {
		var container = document.getElementById('group-repos-container');
		if (!container) return;

		var groupId = container.dataset.groupId;
		var labels = JSON.parse(document.getElementById('group-detail-labels').textContent);
		var el = RepoList.el;

		function render(data) {
			container.textContent = '';
			var empty = el('div');
			empty.appendChild(el('p', 'text-muted', labels.empty));
			var create = el('a', 'btn btn-primary', labels.create);
			create.href = '/dashboard/Repo/New';
			empty.appendChild(create);
			RepoList.list(container, data.repos, data.page, data.hasNext, labels, load, empty);
		}

		function load(rp) {
			fetch('/api/groups/' + groupId + '/repos?rp=' + rp)
				.then(function (r) { return r.json(); })
				.then(render);
		}

		load(0);
	})();
});
