namespace RunicStorage.Runtime;

using ButtonDef = ZInput.ButtonDef;

internal sealed class ControllerBindingState
{
	internal string Signature { get; }
	internal string ModifierName { get; }
	internal string QuickStackName { get; }
	internal string RestockName { get; }
	internal string SortName { get; }
	internal string ConsolidateName { get; }
	internal string SearchName { get; }
	internal ButtonDef Modifier { get; }
	internal ButtonDef QuickStack { get; }
	internal ButtonDef Restock { get; }
	internal ButtonDef Sort { get; }
	internal ButtonDef Consolidate { get; }
	internal ButtonDef Search { get; }
	internal bool ModifierValid => Modifier != null;
	internal bool QuickStackValid => QuickStack != null;
	internal bool RestockValid => Restock != null;
	internal bool SortValid => Sort != null;
	internal bool ConsolidateValid => Consolidate != null;
	internal bool SearchValid => Search != null;
	internal int ValidRouteCount => (QuickStackValid ? 1 : 0) + (RestockValid ? 1 : 0) + (SortValid ? 1 : 0) + (ConsolidateValid ? 1 : 0) + (SearchValid ? 1 : 0);

	internal ControllerBindingState(string signature, string modifierName, string quickStackName, string restockName, string sortName, string consolidateName, string searchName, ButtonDef modifier, ButtonDef quickStack, ButtonDef restock, ButtonDef sort, ButtonDef consolidate, ButtonDef search)
	{
		Signature = signature;
		ModifierName = modifierName;
		QuickStackName = quickStackName;
		RestockName = restockName;
		SortName = sortName;
		ConsolidateName = consolidateName;
		SearchName = searchName;
		Modifier = modifier;
		QuickStack = quickStack;
		Restock = restock;
		Sort = sort;
		Consolidate = consolidate;
		Search = search;
	}
}
