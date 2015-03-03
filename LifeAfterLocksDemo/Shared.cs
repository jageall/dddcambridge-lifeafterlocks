using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleApplication4
{
    internal interface ISeatAllocationService
    {
        Task Allocate(int number, Guid orderId);
        Task<IEnumerable<string>> SeatsForOrder(Guid orderId);
        Task CancelOrder(Guid orderId);
    }

    internal abstract class SeatAllocation
    {
        protected readonly List<string> UnallocatedSeats;
        protected readonly List<AllocatedSeat> AllocatedSeats;

        protected SeatAllocation()
        {
            UnallocatedSeats = new List<string>();
            AllocatedSeats = new List<AllocatedSeat>();
            for (int i = 0; i < 100; i++)
            {
                for (char row = 'a'; row <= 'z'; row++)
                {
                    UnallocatedSeats.Add(string.Format("{0}{1}", row, i));
                }
            }
        }


    }

    internal class AllocatedSeat
    {
        private readonly Guid _orderId;
        private readonly string _seatNumber;

        public AllocatedSeat(Guid orderId, string seatNumber)
        {
            _orderId = orderId;
            _seatNumber = seatNumber;
        }

        public Guid OrderId
        {
            get { return _orderId; }
        }

        public string SeatNumber
        {
            get { return _seatNumber; }
        }
    }

}
