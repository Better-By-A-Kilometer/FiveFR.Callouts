using System.Threading.Tasks;
using CitizenFX.Core;
using FiveFR._API.Classes;
using FiveFR._API.Entities;
using FiveFR._API.Extensions;
using FiveFR._API.Services;

namespace FiveFR.Callouts
{
    public class VehicleTheftInProgress : Callout
    {
        private Vector4 _location = RandomLocationAroundPlayer(250f, 1200f);
        public VehicleTheftInProgress()
        {
            ShortName = "Vehicle Theft Reported";
            CalloutDescription = "Caller reports their vehicle has been stolen. Plate information available upon arrival.";
            ResponseCode = 2;
            StartDistance = 150f;
            InitInfo(_location);
        }

        private Suspect suspect;
        private Vehicle vehicle;

        public override async Task OnAccept()
        {
            vehicle = await SpawnVehicle(RandomHash.Vehicle(), _location);
            suspect = await SpawnSuspect(RandomHash.Ped(), Location.Around(2f), _location.W);
        }

        public override async void OnStart(Ped player)
        {
            vehicle.LockStatus = VehicleLockStatus.Locked;
            vehicle.IsEngineRunning = false;

            var seen = suspect.On.Seen.Await();
            await QueueService.Predicate(() =>
            {
                suspect.Ped.Task.EnterVehicle(vehicle, VehicleSeat.Driver);
                return !seen.IsCompleted;
            }, 2000, 10000);
            

            vehicle.LockStatus = VehicleLockStatus.CanBeBrokenInto;
            suspect.Ped.Task.EnterVehicle(vehicle, VehicleSeat.Driver);
            suspect.On.
        }
    }
}