using System;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;

static string NextPair(RngHost rng, Id stream)
    => $"{rng.Next(stream):R},{rng.Next(stream):R}";

var streamA = new Id("rng.audit.a");
var streamB = new Id("rng.audit.b");
var source = new RngHost(1234UL);
source.Next(streamA); // the saved state contains only A
var saved = new RngStreamsPersistable(source).Save();

// The live instance creates and advances B after the save, then loads the old save.
var live = new RngHost(1234UL);
new RngStreamsPersistable(live).Load(saved);
live.Next(streamB);
live.Next(streamB);
new RngStreamsPersistable(live).Load(saved);

// A clean restore has only the streams present in the saved section.
var clean = new RngHost(1234UL);
new RngStreamsPersistable(clean).Load(saved);

var liveStreamsAfterReload = string.Join(',', live.Streams);
var cleanStreamsBeforeB = string.Join(',', clean.Streams);
var liveContinuation = NextPair(live, streamB);
var cleanContinuation = NextPair(clean, streamB);
Console.WriteLine($"saved={JsonWriter.Write(saved)}");
Console.WriteLine("case1_same_master_seed_stale_stream");
Console.WriteLine($"live_streams_after_reload={liveStreamsAfterReload}");
Console.WriteLine($"clean_streams_before_B={cleanStreamsBeforeB}");
Console.WriteLine($"live_B_after_reload={liveContinuation}");
Console.WriteLine($"clean_B_after_reload={cleanContinuation}");
Console.WriteLine($"equal={liveContinuation == cleanContinuation}");

// A fresh host with another seed restores saved A, but a first use of new B
// cannot be derived from the recording/save's original master seed.
var differentSeed = new RngHost(9999UL);
new RngStreamsPersistable(differentSeed).Load(saved);
var savedAContinuation = NextPair(differentSeed, streamA);
var differentBContinuation = NextPair(differentSeed, streamB);
var originalSeed = new RngHost(1234UL);
new RngStreamsPersistable(originalSeed).Load(saved);
var originalAContinuation = NextPair(originalSeed, streamA);
var originalBContinuation = NextPair(originalSeed, streamB);
Console.WriteLine("case2_different_master_seed_new_stream");
Console.WriteLine($"A_equal_after_restore={savedAContinuation == originalAContinuation}");
Console.WriteLine($"B_different_after_restore={differentBContinuation != originalBContinuation}");
var shortStateAccepted = RngStreamState.TryParse("0-0-0-0", out _);
Console.WriteLine($"short_state_accepted={shortStateAccepted}");
