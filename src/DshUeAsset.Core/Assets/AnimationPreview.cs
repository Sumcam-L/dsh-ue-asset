using System.Security.Cryptography;
using System.Text;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.Animation;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse_Conversion.Animations;
using CUE4Parse_Conversion.Writers.ActorX.Structs.Animations;
using Newtonsoft.Json.Linq;

namespace DshUeAsset.Core.Assets;

/// <summary>
/// Samples a cooked UAnimSequence into bone-local TRS keys in glTF space.
/// The axis conversion matches CUE4Parse's glTF mesh writer: SwapYZ for translation,
/// rotation and scale (rotation also negates W), translation scaled by 0.01.
/// </summary>
public static class AnimationPreview
{
    public const int TargetFps = 30;

    public static (string RelativeEntry, JObject Info, long Bytes) Export(
        IFileProvider provider, string animationPath, string cacheRoot, int targetFps = TargetFps)
    {
        var asset = ObjectReader.Load(provider, animationPath) as UAnimationAsset
            ?? throw AssetException.Invalid($"not an animation asset: {animationPath}");

        CAnimSet animSet;
        try
        {
            animSet = asset.ConvertAnims();
        }
        catch (ArgumentOutOfRangeException ex) when (ex.Message.Contains("Unsupported compressed data type", StringComparison.OrdinalIgnoreCase))
        {
            // Black Myth ships several bone codecs that this CUE4Parse build cannot parse;
            // the sequence conversion throws before any track is produced.
            throw new AssetException(
                AssetErrorKind.Unsupported,
                $"animation codec data is unavailable: the compression settings package is not mounted (include pakchunk0 or open all paks) — {Path.GetFileName(animationPath)}",
                new { path = animationPath });
        }
        if (animSet.Sequences.Count == 0)
            throw AssetException.Invalid($"animation has no sequences: {animationPath}");

        var sequence = animSet.Sequences[0];
        var skeleton = animSet.Skeleton.ReferenceSkeleton;
        var bones = skeleton.FinalRefBoneInfo;
        var rest = skeleton.FinalRefBonePose;
        var fps = sequence.FramesPerSecond > 0 ? sequence.FramesPerSecond : 30f;
        var step = Math.Max(1, (int)Math.Round(fps / Math.Max(1, targetFps)));

        var frames = new List<int>();
        for (var frame = 0; frame < sequence.NumFrames; frame += step) frames.Add(frame);
        if (frames.Count == 0 || frames[^1] != sequence.NumFrames - 1) frames.Add(Math.Max(0, sequence.NumFrames - 1));

        var times = new JArray(frames.Select(f => Math.Round(f / fps, 4)));
        var tracks = new JArray();

        for (var boneIndex = 0; boneIndex < bones.Length && boneIndex < sequence.Tracks.Count; boneIndex++)
        {
            if (sequence.OriginalSequence.FindTrackForBoneIndex(boneIndex) < 0) continue;
            if (boneIndex >= rest.Length) continue;

            var p = new JArray();
            var r = new JArray();
            var s = new JArray();
            var restTransform = rest[boneIndex];

            foreach (var frame in frames)
            {
                var position = restTransform.Translation;
                var rotation = restTransform.Rotation;
                var scale = restTransform.Scale3D;
                sequence.Tracks[boneIndex].GetBoneTransform(frame, sequence.NumFrames, ref rotation, ref position, ref scale);

                var gp = Swap(position * 0.01f);
                var gq = Swap(rotation);
                var gs = Swap(scale);

                p.Add(Math.Round(gp.X, 4)); p.Add(Math.Round(gp.Y, 4)); p.Add(Math.Round(gp.Z, 4));
                r.Add(Math.Round(gq.X, 4)); r.Add(Math.Round(gq.Y, 4)); r.Add(Math.Round(gq.Z, 4)); r.Add(Math.Round(gq.W, 4));
                s.Add(Math.Round(gs.X, 4)); s.Add(Math.Round(gs.Y, 4)); s.Add(Math.Round(gs.Z, 4));
            }

            tracks.Add(new JObject
            {
                ["bone"] = bones[boneIndex].Name.Text,
                ["parent"] = bones[boneIndex].ParentIndex,
                ["p"] = p,
                ["r"] = r,
                ["s"] = s
            });
        }

        var clip = new JObject
        {
            ["name"] = sequence.Name,
            ["path"] = animationPath,
            ["duration"] = Math.Round(sequence.AnimEndTime > 0 ? sequence.AnimEndTime : sequence.NumFrames / fps, 3),
            ["fps"] = Math.Round(fps, 3),
            ["times"] = times,
            ["tracks"] = tracks
        };

        var json = clip.ToString(Newtonsoft.Json.Formatting.None);
        var directory = Path.Combine(cacheRoot, "anim");
        Directory.CreateDirectory(directory);
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(animationPath + "|" + sequence.NumFrames + "|" + targetFps)))[..12].ToLowerInvariant();
        var safeName = string.Concat(Path.GetFileNameWithoutExtension(animationPath).Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
        var fileName = safeName + "-" + hash + ".json";
        var fullPath = Path.Combine(directory, fileName);
        File.WriteAllText(fullPath, json);

        var info = new JObject
        {
            ["name"] = sequence.Name,
            ["path"] = animationPath,
            ["duration"] = clip["duration"],
            ["fps"] = clip["fps"],
            ["bones"] = tracks.Count,
            ["samples"] = frames.Count,
            ["bytes"] = json.Length
        };
        return ("anim/" + fileName, info, json.Length);
    }

    private static FVector Swap(FVector v) => new(v.X, v.Z, v.Y);
    private static FQuat Swap(FQuat q) => new(q.X, q.Z, q.Y, -q.W);
}