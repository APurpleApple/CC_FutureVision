using HarmonyLib;
using Microsoft.Extensions.Logging;
using Nanoray.PluginManager;
using Nickel;
using System;
using System.Collections.Generic;
using System.Collections.Frozen;
using Myosotis.DeepCopy;

namespace APurpleApple.FutureVision;

public sealed class PMod : SimpleMod, OnMouseDown
{
    internal static PMod Instance { get; private set; } = null!;

    public List<Tuple<Type, Predicate<CardAction>?>> actionsDisallowedFromPreview = new List<Tuple<Type, Predicate<CardAction>?>>();
    public List<Type> disallowedArtifacts = new();

    public static Dictionary<string, ISpriteEntry> sprites = new();

    public void RegisterSprite(string key, string fileName, IPluginPackage<IModManifest> package)
    {
        sprites.Add(key, Helper.Content.Sprites.RegisterSprite(package.PackageRoot.GetRelativeFile("Sprites/" + fileName)));
    }

    public void AddDisallowedActionPreview(Type actionType, Predicate<CardAction>? filter = null)
    {
        actionsDisallowedFromPreview.Add(new Tuple<Type, Predicate<CardAction>?> ( actionType, filter ));
    }

    private void Patch()
    {
        Harmony harmony = new("APurpleApple.FutureVision");
        harmony.PatchAll();
    }

    public void OnMouseDown(G g, Box b)
    {
        if (b.key!.Value.str == "FuturePreview")
        {
            HarmonyPatches.isPreviewEnabled = !HarmonyPatches.isPreviewEnabled;
        }
    }

    public PMod(IPluginPackage<IModManifest> package, IModHelper helper, ILogger logger) : base(package, helper, logger)
    {
        DeepCopy.Init();

        Instance = this;

        RegisterSprite("button_vis", "buttons/vision.png", package);
        RegisterSprite("button_vis_on", "buttons/vision_on.png", package);
        RegisterSprite("button_err", "buttons/vision_error.png", package);
        RegisterSprite("button_err_on", "buttons/vision_error_on.png", package);
        RegisterSprite("button_dis", "buttons/vision_disabled.png", package);
        RegisterSprite("button_dis_on", "buttons/vision_disabled_on.png", package);
        RegisterSprite("button_broke", "buttons/vision_broke.png", package);

        AddDisallowedActionPreview(typeof(AMove), (x) => x is AMove a && a.isRandom);
        AddDisallowedActionPreview(typeof(ADrawCard));

        Patch();
    }
}
