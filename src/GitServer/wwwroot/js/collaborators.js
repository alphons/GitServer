document.addEventListener('DOMContentLoaded', function () {
	var input = document.getElementById('collaboratorName');
	if (!input) return;
	var searchUrl = '/api/repos/' + encodeURIComponent(input.dataset.userName) + '/' + encodeURIComponent(input.dataset.repoName) + '/user-search';
	initCollaboratorSearch('collaboratorName', 'collaboratorResults', searchUrl);
});
