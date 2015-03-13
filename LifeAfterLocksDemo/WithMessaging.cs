using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ConsoleApplication4
{
    class MessagingSeatAllocationService : ISeatAllocationService
    {
        private readonly TaskBridge _bridge;

        public MessagingSeatAllocationService(TaskBridge bridge)
        {
            _bridge = bridge;
        }

        public Task Allocate(int number, Guid orderId)
        {
            return _bridge.Create<AllocateSeats, SeatsAllocated>(
                () => new AllocateSeats(orderId, number));
        }

        public Task<IEnumerable<string>> SeatsForOrder(Guid orderId)
        {
            return _bridge.Create<GetSeatsForOrder, SeatsForOrder, IEnumerable<string>>(
                () => new GetSeatsForOrder(orderId),
                m => m.Seats.Select(x => x.SeatNumber)
                );
        }

        public Task CancelOrder(Guid orderId)
        {
            return _bridge.Create<CancelOrder, OrderCancelled>(
                () => new CancelOrder(orderId));
        }
    }

    internal class SeatAllocationWithMessaging : SeatAllocation,
        IHandle<AllocateSeats>,
        IHandle<GetSeatsForOrder>,
        IHandle<CancelOrder>
    {
        private readonly IPublisher _publisher;

        public SeatAllocationWithMessaging(IPublisher publisher)
        {
            _publisher = publisher;
        }
        public void Handle(AllocateSeats msg)
        {
            var seats = UnallocatedSeats.GetRange(0, msg.Amount);
            UnallocatedSeats.RemoveRange(0, msg.Amount);
            AllocatedSeats.AddRange(seats.Select(x=> new AllocatedSeat(msg.OrderId, x)));
            _publisher.Publish(new SeatsAllocated(msg.OrderId));
        }

        public void Handle(GetSeatsForOrder msg)
        {
            var seats = GetSeatsForOrder(msg.OrderId);
            _publisher.Publish(new SeatsForOrder(msg.OrderId, seats.ToArray()));
        }

        private IEnumerable<AllocatedSeat> GetSeatsForOrder(Guid orderId)
        {
            return AllocatedSeats.Where(x => MatchOrder(orderId, x));
        }

        private static bool MatchOrder(Guid orderId, AllocatedSeat x)
        {
            return x.OrderId == orderId;
        }

        public void Handle(CancelOrder msg)
        {
            var seats = GetSeatsForOrder(msg.OrderId);
            AllocatedSeats.RemoveAll(x=>MatchOrder(msg.OrderId, x));
            UnallocatedSeats.AddRange(seats.Select(x=>x.SeatNumber));
            _publisher.Publish(new OrderCancelled(msg.OrderId));
        }
    }

    internal class SeatsForOrder : Message
    {
        private readonly Guid _orderId;
        private readonly IReadOnlyList<AllocatedSeat> _seats;

        public SeatsForOrder(Guid orderId, IReadOnlyList<AllocatedSeat> seats)
        {
            _orderId = orderId;
            Correlation = _orderId;
            _seats = seats;
        }

        public IReadOnlyList<AllocatedSeat> Seats
        {
            get { return _seats; }
        }

        public Guid OrderId
        {
            get { return _orderId; }
        }
    }

    internal class CancelOrder : Message
    {
        private readonly Guid _orderId;

        public CancelOrder(Guid orderId)
        {
            _orderId = orderId;
            Correlation = _orderId;
        }

        public Guid OrderId
        {
            get { return _orderId; }
        }
    }

    internal class OrderCancelled : Message
    {
        private readonly Guid _orderId;

        public OrderCancelled(Guid orderId)
        {
            _orderId = orderId;
            Correlation = _orderId;
        }

        public Guid OrderId
        {
            get { return _orderId; }
        }
    }

    internal class GetSeatsForOrder : Message
    {
        private readonly Guid _orderId;

        public GetSeatsForOrder(Guid orderId)
        {
            _orderId = orderId;
            Correlation = _orderId;
        }

        public Guid OrderId
        {
            get { return _orderId; }
        }
    }

    class SeatsAllocated : Message
    {
        private readonly Guid _orderId;

        public SeatsAllocated(Guid orderId)
        {
            _orderId = orderId;
            Correlation = _orderId;
        }

        public Guid OrderId
        {
            get { return _orderId; }
        }
    }

    internal class AllocateSeats : Message
    {
        private readonly Guid _orderId;
        private readonly int _amount;

        public AllocateSeats(Guid orderId, int amount)
        {
            _orderId = orderId;
            Correlation = _orderId;
            _amount = amount;
        }

        public Guid OrderId
        {
            get { return _orderId; }
        }

        public int Amount
        {
            get { return _amount; }
        }
    }
}
