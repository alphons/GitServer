document.addEventListener('DOMContentLoaded', function () {
	var input = document.getElementById('profile-repo-search');
	var container = document.getElementById('profile-repos-container');
	if (!input || !container) return;

	var apiUrl = container.dataset.apiUrl;
	var labels = JSON.parse(document.getElementById('profile-labels').textContent);
	var el = RepoList.el;
	var debounceTimer = null;
	var query = input.value;
	var seq = 0;

	function section(title, count) {
		var d = el('details', 'collapsible-section');
		d.open = true;
		d.appendChild(el('summary', null, title + ' (' + count + ')'));
		return d;
	}

	function render(data) {
		container.textContent = '';

		if (data.query) {
			var info = el('p', 'search-results-info', data.resultCount + ' ' + labels.resultsFor + ' "');
			info.appendChild(el('strong', null, data.query));
			info.appendChild(document.createTextNode('"'));
			container.appendChild(info);
		}

		var own = section(data.isOwner ? labels.ownTitle : labels.repos, data.totalCount);
		RepoList.list(own, data.repos, data.page, data.hasNext, labels,
			function (p) { load(query, p, data.groupPage); },
			el('div', 'empty-state', data.query ? labels.noResults : labels.noRepos));
		container.appendChild(own);

		if (data.isOwner) {
			var grp = section(labels.groupTitle, data.groupTotalCount);
			RepoList.list(grp, data.groupRepos, data.groupPage, data.groupHasNext, labels,
				function (gp) { load(query, data.page, gp); },
				el('p', 'text-muted', data.query ? labels.noResults : labels.groupEmpty));
			container.appendChild(grp);
		}
	}

	function load(q, p, gp) {
		var mine = ++seq;
		var url = apiUrl + '?q=' + encodeURIComponent(q) + '&p=' + p + '&gp=' + gp;
		fetch(url)
			.then(function (r) { return r.json(); })
			.then(function (data) { if (mine === seq) render(data); });
	}

	input.addEventListener('input', function () {
		query = input.value;
		clearTimeout(debounceTimer);
		debounceTimer = setTimeout(function () { load(query, 0, 0); }, 250);
	});

	load(query, 0, 0);
});
