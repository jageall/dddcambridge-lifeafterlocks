using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleApplication4
{
    internal class Program
    {
        private static void Main()
        {
            ISeatAllocationService allocator = new OriginalSeatAllocationService();
            RunSeatAllocation(allocator);

            //ISeatAllocationService allocator = CreateMessagingAllocator();
            //RunSeatAllocation(allocator);
        }

        private static ISeatAllocationService CreateMessagingAllocator()
        {
            var bus = new InMemoryBus();
            var bridge = new TaskBridge(bus);
            var service = new SeatAllocationWithMessaging(bus);
            bus.Subscribe<AllocateSeats>(service);
            bus.Subscribe<GetSeatsForOrder>(service);
            bus.Subscribe<CancelOrder>(service);
            bus.SubscribeAll(new CorrelationManager(bus));
            var allocator = new MessagingSeatAllocationService(bridge);
            bus.Start();
            return allocator;
        }

        private static void RunSeatAllocation(ISeatAllocationService allocator)
        {
            var tasks = new Task<string>[1000];
            for (int i = 0; i < 1000; i++)
            {
                tasks[i] = CreateBookingTask(allocator);
            }
            Task.WhenAll(tasks).ContinueWith(x => { foreach (var s in x.Result) {
                Console.WriteLine(s);
            } }).Wait();
            Console.WriteLine("Done {0}", allocator.GetType());
        }

        static readonly Random Random = new Random();
        
        static async Task<string> CreateBookingTask(ISeatAllocationService service)
        {
            await Task.Yield();
            var id = Guid.NewGuid();
            await service.Allocate(Random.Next(1, 5), id);
            var results = await service.SeatsForOrder(id);
            if (Random.Next(0, 100) < 24)
            {
                await service.CancelOrder(id);
                return "Cancelled " + string.Join(",", results);
            }
            return "Booked " + string.Join(",", results);
        }
    }
    
    
}
