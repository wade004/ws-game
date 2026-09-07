using Core.Foundation.Common;
using Presentation.Render;

var clipId = new Id("anim.test_clip");
var clips = new Dictionary<Id, FrameAnimClip>
{
    [clipId] = new FrameAnimClip(
        clipId,
        frameCount: 10,
        frameRate: 10.0,
        keyframes: new Dictionary<string, int> { [FrameAnimClip.HitFrameMarker] = 2 })
};
var player = new FrameAnimPlayer(clips);
var frames = new List<int>();
var events = new List<string>();
player.FrameChanged += frame => frames.Add(frame);
player.OnAnimEvent(name => events.Add(name));
player.Play(clipId, loop: true, speed: 1.0);
player.Update(0.35);

Console.WriteLine($"PLAY_UPDATE elapsed=0.35 frame_rate=10 total_frames=10 current_frame={player.CurrentFrame}");
Console.WriteLine($"FRAME_CHANGED_SEQUENCE={string.Join(",", frames)}");
Console.WriteLine($"ANIM_EVENTS={string.Join(",", events)} hit_event_count={events.Count(name => name == FrameAnimClip.HitFrameMarker)}");
Console.WriteLine("EXPECTED current_frame=3; hit_frame event would require crossing frame 2, but implementation only fires at exact landed frame");
