using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CitizenFX.Core;
using FiveFR._API.Attributes;
using FiveFR._API.Classes;
using FiveFR._API.Entities;
using FiveFR._API.Extensions;
using FiveFR._API.Services;

namespace FiveFR.Callouts
{
    [Guid("A2E3D303-BA97-4C0A-BB1F-CC07A80A7E0A")]
    [AddonProperties("Vehicle Theft", "^1FiveFR Team", "1.0")]
    public class VehicleTheftInProgress : Callout
    {
        private Vector4 _location = RandomLocationAroundPlayer(250f, 1200f, LocationSurface.Road);
        public VehicleTheftInProgress()
        {
            ShortName = "Vehicle Theft Reported";
            CalloutDescription = "Caller reports their vehicle has been stolen. Plate information available upon arrival.";
            ResponseCode = 2;
            StartDistance = 250f;
            InitInfo(_location.Around(50f));
        }

        private Suspect _suspect = null;
        private Vehicle _vehicle = null;

        public override async Task OnAccept()
        {
            InitBlip(150f);
            _vehicle = await SpawnVehicle(RandomHash.Vehicle(), _location);
            _suspect = await SpawnSuspect(RandomHash.Ped(), (Vector3)_location.Around(2f), _location.W);
        }

        public override async void OnStart(Ped player)
        {
            
            _vehicle.LockStatus = VehicleLockStatus.Locked;
            _vehicle.IsEngineRunning = false;

            var seen = _suspect.On.Seen.Await();
            await QueueService.Predicate(() =>
            {
                if (_suspect.Ped.Exists())
                {
                    _suspect.Ped.Task.EnterVehicle(_vehicle, VehicleSeat.Driver);
                }
                return !seen.IsCompleted;
            }, 2000, 10000);
            

            _vehicle.LockStatus = VehicleLockStatus.CanBeBrokenInto;
            _ = _suspect.Move.EnterVehicle(_vehicle, run: true);
            bool success = await _suspect.On.EnteredVehicle(_vehicle).Await(TimeSpan.FromSeconds(5000));

            if (!success)
            {
                bool gotSeat = _vehicle.TryGetNextSeatFree(out var seat);
                _suspect.Ped.SetIntoVehicle(_vehicle, gotSeat ? seat : VehicleSeat.Driver);   
            }
            
            _suspect.Flee();
            await _suspect.On.Seen.Await();
            Marker.Alpha = 0;
        }
    }
}