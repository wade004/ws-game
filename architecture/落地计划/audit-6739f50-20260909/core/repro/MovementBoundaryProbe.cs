using System;
using System.Collections.Generic;
using Adapters.Stub;
using Core.Carriers.Common;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Numbers.StatBlock;
using Core.Rules.Common;

sealed class BoundaryStatHost : IStatHost
{
    private readonly Dictionary<(Id, Id), double> _bases = new();
    public void RegisterUnit(Id unitId) { }
    public void UnregisterUnit(Id unitId) { }
    public bool IsRegistered(Id unitId) => true;
    public void SetBase(Id unitId, Id stat, double value) => _bases[(unitId, stat)] = value;
    public double GetBase(Id unitId, Id stat) => _bases.TryGetValue((unitId, stat), out var value) ? value : 0;
    public double GetStat(Id unitId, Id stat) => GetBase(unitId, stat);
    public void AddModifier(Id unitId, StatModifier modifier) { }
    public void RemoveModifiersBySource(Id unitId, Id sourceId) { }
    public IReadOnlyList<StatModifier> GetModifiers(Id unitId, Id stat) => Array.Empty<StatModifier>();
}

sealed class BoundaryAuraQuery : IAuraQuery
{
    public bool HasAura(Id unitId, Id auraDefId) => false;
    public int GetStacks(Id unitId, Id auraDefId) => 0;
    public ControlFlags GetControlFlags(Id unitId) => ControlFlags.None;
    public bool IsImmune(Id unitId, Id school, EffectKind kind) => false;
    public double ConsumeAbsorb(Id unitId, Id school, double amount) => 0;
    public IReadOnlyList<Id> GetActiveAuraDefs(Id unitId) => Array.Empty<Id>();
}

sealed class MovementBoundaryFixture
{
    public WorldSim World = null!;
    public WorldUnitAccess Units = null!;
    public MovementHost Host = null!;
    public PlayerUnit Player = null!;
}

static class MovementBoundaryProbe
{
    static readonly Id Map = new("map.movement.boundary");
    static readonly Id Faction = new("fac.movement.boundary");
    static readonly Id Archetype = new("arch.class.movement.boundary");
    static readonly Id Unit = new("unit.movement.boundary");

    public static void Main()
    {
        Console.WriteLine("MOVEMENT-BOUNDARY-PROBE baseline=6739f50 version=1.11.0");
        RunRepeatedMove(1);
        RunRepeatedMove(2);
        RunRepeatedMove(3);
        RunStopAfterTwoMoves();
        RunTwoMovesAfterStop();
    }

    static MovementBoundaryFixture Build()
    {
        var bus = new EventBus(
            EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
            new EventBusOptions { StrictCatalog = false });
        var world = new WorldSim(bus);
        var player = new PlayerUnit(Unit, Map, Faction, Archetype) { Position = Vec2.Zero };
        world.AddEntity(player);
        var units = new WorldUnitAccess(world);
        var stats = new BoundaryStatHost();
        stats.SetBase(Unit, new MovementOptions().MoveSpeedStat, 10.0);
        var host = new MovementHost(world);
        var handler = new MovementTickHandler(units, stats, new BoundaryAuraQuery(), host, bus);
        world.RegisterPhaseHandler(TickPhase.MovementAndNavigation, handler);
        return new MovementBoundaryFixture { World = world, Units = units, Host = host, Player = player };
    }

    static void RunRepeatedMove(int count)
    {
        var fx = Build();
        for (var i = 0; i < count; i++)
            fx.Host.Request(MoveRequest.ToTarget(Unit, new Vec2(100, 0)));
        fx.World.Tick(SimStep.Continuous(0.1));
        Console.WriteLine($"REPEATED-MOVE count={count};speed=10;dt=0.1;actual_x={fx.Units.GetPosition(Unit).X};path_active={fx.Player.MovementState.CurrentPath != null}");
    }

    static void RunStopAfterTwoMoves()
    {
        var fx = Build();
        var stopped = 0;
        fx.Host.OnMoveStopped += (_, _, _) => stopped++;
        fx.Host.Request(MoveRequest.ToTarget(Unit, new Vec2(100, 0)));
        fx.Host.Request(MoveRequest.ToTarget(Unit, new Vec2(100, 0)));
        fx.Host.Stop(Unit);
        fx.World.Tick(SimStep.Continuous(0.1));
        Console.WriteLine($"STOP-AFTER-TWO-MOVES actual_x={fx.Units.GetPosition(Unit).X};stopped_callbacks={stopped};path_active={fx.Player.MovementState.CurrentPath != null};mode={fx.Player.MovementState.Mode}");
    }

    static void RunTwoMovesAfterStop()
    {
        var fx = Build();
        var stopped = 0;
        MoveStopReason? stopReason = null;
        fx.Host.OnMoveStopped += (_, _, reason) => { stopped++; stopReason = reason; };
        fx.Host.Stop(Unit);
        fx.Host.Request(MoveRequest.ToTarget(Unit, new Vec2(100, 0)));
        fx.Host.Request(MoveRequest.ToTarget(Unit, new Vec2(100, 0)));
        fx.World.Tick(SimStep.Continuous(0.1));
        Console.WriteLine($"TWO-MOVES-AFTER-STOP actual_x={fx.Units.GetPosition(Unit).X};stopped_callbacks={stopped};stop_reason={stopReason};path_active={fx.Player.MovementState.CurrentPath != null};mode={fx.Player.MovementState.Mode}");
    }
}
