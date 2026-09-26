using GitServer.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Routing;

namespace GitServer.Extensions;

/// <summary>Puts the configurable git path prefix (e.g. "/git") in front of the git smart-HTTP and Git LFS routes only,
/// so the JSON API under /api is never moved along with it.</summary>
public class GitRoutePrefixConvention(string prefix) : IControllerModelConvention
{
	public void Apply(ControllerModel controller)
	{
		if (prefix.Length == 0 || (controller.ControllerType != typeof(GitController) && controller.ControllerType != typeof(LfsController))) return;

		var prefixModel = new AttributeRouteModel(new RouteAttribute(prefix));
		foreach (var selector in controller.Actions.SelectMany(a => a.Selectors).Where(s => s.AttributeRouteModel != null))
			selector.AttributeRouteModel = AttributeRouteModel.CombineAttributeRouteModel(prefixModel, selector.AttributeRouteModel);
	}
}
