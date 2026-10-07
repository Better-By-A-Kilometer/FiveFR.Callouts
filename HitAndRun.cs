using System;
using System.Threading.Tasks;
using CitizenFX.Core;
using CitizenFX.Core.Native;
using FiveFR._API.Attributes;
using FiveFR._API.Classes;
using FiveFR._API.Entities;
using FiveFR._API.Extensions;
using FiveFR._API.Radio;
using FiveFR._API.Services;

namespace FiveFR.Callouts;

[AddonProperties("Hit and Run", "^1FiveFR Team", "1.0")]
public class HitAndRun : Callout
{
    private enum Severity { Minor, Serious, Critical }

    private Vector4 _location = RandomLocationAroundPlayer(150f, 1500f, LocationSurface.Road);
    private readonly Severity _severity = RandomHash.From<Severity>();

    public HitAndRun()
    {
        ShortName = "Hit and run";
        CalloutDescription = "Caller reports a pedestrian was struck by a vehicle that fled the scene. Victim may require medical attention. Suspect vehicle description available upon arrival.";
        ResponseCode = 3;
        StartDistance = 120f;
        InitInfo(_location);
    }

    private Suspect _suspect = null;
    private CalloutVehicle _vehicle = null;
    private Civilian _victim = null;
    private Civilian _witness = null;

    public override async Task OnAccept()
    {
        InitBlip(StartDistance);
        
        // Victim lies where they were struck; the witness stands beside them.
        var scene = ((Vector3)_location).ClosestPedPlacement();
        DiagnosticLog.WriteLine($"location={(Vector3)_location} scene={scene}");
        _victim = await SpawnCivilian(RandomHash.Ped(), scene, _location.W, CivilianRole.Victim);
        _witness = await SpawnCivilian(RandomHash.Ped(), scene.Around(2f).ClosestPedPlacement(), _location.W, CivilianRole.Witness);
        
        // The driver fled and has ditched the car a few blocks away.
        var dump = ((Vector3)_location).RandomLocationWithHeadingAround(250f, 600f, LocationSurface.Road)
            .ClosestParkedCarPlacement();
        _vehicle = await SpawnCalloutVehicle(RandomHash.Vehicle(), dump);
        // The car can be hundreds of metres from the officer until they go after it, so its look
        // is set once it reaches this client. The colour is picked now so the witness can
        // describe it before then.
        _vehicle?.WhenHere(DressVehicle);
        _suspect = await SpawnSuspect(RandomHash.Ped(), ((Vector3)dump).Around(3f).ClosestPedPlacement(), dump.W);
    }

    private static readonly VehicleColor[] Colors =
    {
        VehicleColor.MetallicBlack, VehicleColor.MetallicSilver, VehicleColor.MetallicRed,
        VehicleColor.MetallicDarkBlue, VehicleColor.MetallicWhite, VehicleColor.MetallicDarkGreen,
    };

    private static readonly string[] ColorNames = { "black", "silver", "red", "dark blue", "white", "dark green" };

    private readonly int _color = rnd.Next(Colors.Length);

    private void DressVehicle(Vehicle vehicle)
    {
        vehicle.Mods.PrimaryColor = Colors[_color];
        // Front-end damage from the impact.
        API.SetVehicleDamage(vehicle.Handle, 0f, 2f, 0.2f, 400f, 150f, true);
        vehicle.DirtLevel = 8f;
    }

    public override async void OnStart(Ped player)
    { 
        _suspect.Ped.SetIntoVehicle(_vehicle, VehicleSeat.Driver);
        ApplyInjuries(_victim.Ped);
        
        await PoseVictim(_victim.Ped);
        _witness.Ped.Task.TurnTo(_victim, 850);
        _ = BaseScript.Delay(850).ContinueWith(_ => PoseWitness(_witness.Ped));

        var vBlip = _victim.Blip.Show(BlipColor.MichaelBlue, name: "Victim");
        var wBlip = _witness.Blip.Show(BlipColor.Yellow, name: "Witness");
        wBlip.IsFlashing = true;
        TrackBlip(vBlip);
        string description = $"{ColorNames[_color]} {_vehicle?.DisplayName ?? "car"}";
        var teller = _severity == Severity.Minor ? (CalloutPed)_victim : _witness;
        await teller.On.Approached(5f);
        await teller.Speak($"It was a {description}! They didn't even slow down, went off that way.", Game.PlayerPed);
        _suspect.Demeanor = Demeanor.Evasive.With(flight: Tendency.Percent(90));
        RadioApi.TransmitAsOfficer($"Dispatch, be advised. Suspect's vehicle is a {description}.", $"10-4. Suspect's vehicle is a {description}, {RadioApi.Callsign}");
        await _suspect.On.Seen.Await();
        _suspect.Blip.Show(BlipColor.Red, name: "Suspect");
    }
    

    private void ApplyInjuries(Ped victim)
    {
        victim.BlockPermanentEvents = true;
        switch (_severity)
        {
            case Severity.Minor:
                API.ApplyPedDamagePack(victim.Handle, "Car_Crash_Light", 0f, 1f);
                victim.Health = 170;
                break;
            case Severity.Serious:
                API.ApplyPedDamagePack(victim.Handle, "BigHitByVehicle", 0f, 1f);
                victim.Health = 130;
                break;
            case Severity.Critical:
                API.ApplyPedDamagePack(victim.Handle, "Car_Crash_Heavy", 0f, 1f);
                API.ApplyPedDamagePack(victim.Handle, "BigHitByVehicle", 0f, 1f);
                victim.Health = 110;
                break;
        }
    }

    private async Task PoseVictim(Ped victim)
    {
        switch (_severity)
        {
            case Severity.Minor:
                // Dazed and limping around the scene.
                if (await LoadAnimSet("move_m@injured"))
                    API.SetPedMovementClipset(victim.Handle, "move_m@injured", 1f);
                KeepScenario(victim, "WORLD_HUMAN_STUPOR");
                break;
            case Severity.Serious:
                // Conscious and in pain on the ground.
                if (!await KeepLoopedAnim(victim, "combat@damage@writhe", "writhe_loop"))
                    KeepScenario(victim, "WORLD_HUMAN_BUM_SLUMPED");
                break;
            case Severity.Critical:
                // Unresponsive.
                KeepScenario(victim, "WORLD_HUMAN_SUNBATHE_BACK");
                break;
        }
    }

    private void PoseWitness(Ped witness)
    {
        string scenario = _severity switch
        {
            Severity.Minor => "WORLD_HUMAN_STAND_MOBILE",
            Severity.Serious => "CODE_HUMAN_MEDIC_KNEEL",
            _ => "CODE_HUMAN_MEDIC_TEND_TO_DEAD",
        };
        KeepScenario(witness, scenario);
    }

    private void KeepScenario(Ped ped, string scenario) =>
        _ = KeepPosed(ped,
            () => API.TaskStartScenarioInPlace(ped.Handle, scenario, 0, false),
            () => API.IsPedUsingScenario(ped.Handle, scenario));

    private async Task<bool> KeepLoopedAnim(Ped ped, string dict, string clip)
    {
        if (!API.DoesAnimDictExist(dict)) return false;
        API.RequestAnimDict(dict);
        for (int i = 0; i < 50 && !API.HasAnimDictLoaded(dict); i++)
            await BaseScript.Delay(100);
        if (!API.HasAnimDictLoaded(dict)) return false;

        // The dict stays loaded while the pose is held, so it can be replayed after a bump.
        _ = HoldThenRelease();
        return true;

        async Task HoldThenRelease()
        {
            await KeepPosed(ped,
                () => ped.Task.PlayAnimation(dict, clip, 8f, -1, AnimationFlags.Loop),
                () => API.IsEntityPlayingAnim(ped.Handle, dict, clip, 3));
            API.RemoveAnimDict(dict);
        }
    }

    // Bumping into or stepping over a ped is a temporary event, which BlockPermanentEvents doesn't
    // stop - the game drops their scenario/anim and never gives it back. Re-apply it until the
    // callout ends, or until they've been moved off the spot (directed to walk, fled, etc).
    private async Task KeepPosed(Ped ped, Action apply, Func<bool> isPosed)
    {
        var anchor = ped.Position;
        ped.BlockPermanentEvents = true;
        API.SetPedCanRagdollFromPlayerImpact(ped.Handle, false);
        API.SetPedCanEvasiveDive(ped.Handle, false);
        apply();

        while (!Ended && ped.Exists() && !ped.IsDead)
        {
            await BaseScript.Delay(1000);
            if (ped.IsRagdoll || isPosed()) continue;
            if (ped.Position.DistanceToSquared(anchor) > 3f * 3f) break;
            apply();
        }
    }

    private static async Task<bool> LoadAnimSet(string set)
    {
        API.RequestAnimSet(set);
        for (int i = 0; i < 50 && !API.HasAnimSetLoaded(set); i++)
            await BaseScript.Delay(100);
        return API.HasAnimSetLoaded(set);
    }
}
