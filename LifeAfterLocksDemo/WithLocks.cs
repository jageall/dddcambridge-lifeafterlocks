using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ConsoleApplication4
{
    internal class OriginalSeatAllocationService : ISeatAllocationService
    {
        private readonly SeatAllocationWithLocks _allocationService;

        public OriginalSeatAllocationService()
        {
            _allocationService = new SeatAllocationWithLocks();
        }

        public Task Allocate(int number, Guid orderId)
        {
            return Task.Run(() => _allocationService.Allocate(number, orderId));
        }

        public Task<IEnumerable<string>> SeatsForOrder(Guid orderId)
        {
            return Task.Run(() => _allocationService.GetSeatsForOrder(orderId).Select(x => x.SeatNumber));
        }

        public Task CancelOrder(Guid orderId)
        {
            return Task.Run(() => _allocationService.CancellOrder(orderId));
        }
    }
    internal class SeatAllocationWithLocks : SeatAllocation
    {
        public void Allocate(int number, Guid id)
        {
            lock (UnallocatedSeats)
                lock (AllocatedSeats)
                {
                    var allocated = UnallocatedSeats.GetRange(0, number);
                    UnallocatedSeats.RemoveRange(0, number);
                    AllocatedSeats.AddRange(allocated.Select(x => new AllocatedSeat(id, x)));
                }
        }

        public IEnumerable<AllocatedSeat> GetSeatsForOrder(Guid id)
        {
            lock (AllocatedSeats)
            {
                return AllocatedSeats.Where(x => x.OrderId == id).ToArray();
            }
        }

        public void CancellOrder(Guid id)
        {
            lock (UnallocatedSeats)
                lock (AllocatedSeats)
                {
                    var seats = AllocatedSeats.Where(x => x.OrderId == id).Select(x => x.SeatNumber);
                    UnallocatedSeats.AddRange(seats);
                    AllocatedSeats.RemoveAll(x => x.OrderId == id);
                }
        }
    }

}
