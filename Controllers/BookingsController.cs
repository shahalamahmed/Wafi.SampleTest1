using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Globalization;
using Wafi.SampleTest.Dtos;
using Wafi.SampleTest.Entities;

namespace Wafi.SampleTest.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BookingsController : ControllerBase
    {
        private readonly WafiDbContext _context;

        public BookingsController(WafiDbContext context)
        {
            _context = context;
        }

        [HttpGet("Booking")]
        public async Task<ActionResult<IEnumerable<BookingCalendarDto>>> GetCalendarBookings([FromQuery] BookingFilterDto input)
        {
            if (input.StartBookingDate > input.EndBookingDate)
            {
                return BadRequest("Start date cannot be after end date.");
            }

            try
            {

                var bookings = await _context.Bookings
                    .Where(b => b.CarId == input.CarId &&
                                b.BookingDate >= input.StartBookingDate &&
                                b.BookingDate <= input.EndBookingDate)
                    .Include(b => b.Car)
                    .ToListAsync();

                if (!bookings.Any())
                {
                    return NotFound("No bookings found for the given criteria.");
                }

                var calendarBookings = new List<BookingCalendarDto>();

                foreach (var booking in bookings)
                {
                    if (booking.RepeatOption == RepeatOption.DoesNotRepeat)
                    {
                        calendarBookings.Add(MapToCalendarDto(booking));
                    }
                    else if (booking.RepeatOption == RepeatOption.Daily)
                    {
                        var currentDate = booking.BookingDate;
                        var endDate = booking.EndRepeatDate ?? input.EndBookingDate;

                        while (currentDate <= endDate)
                        {
                            if (currentDate >= input.StartBookingDate)
                            {
                                calendarBookings.Add(new BookingCalendarDto
                                {
                                    BookingDate = currentDate,
                                    StartTime = booking.StartTime,
                                    EndTime = booking.EndTime,
                                    CarModel = booking.Car?.Model
                                });
                            }
                            currentDate = currentDate.AddDays(1);
                        }
                    }
                    else if (booking.RepeatOption == RepeatOption.Weekly)
                    {
                        var currentDate = booking.BookingDate;
                        var endDate = booking.EndRepeatDate ?? input.EndBookingDate;

                        while (currentDate <= endDate)
                        {
                            if (currentDate >= input.StartBookingDate)
                            {
                                if (!calendarBookings.Any(b => b.BookingDate == currentDate && b.StartTime == booking.StartTime && b.EndTime == booking.EndTime))
                                {
                                    calendarBookings.Add(new BookingCalendarDto
                                    {
                                        BookingDate = currentDate,
                                        StartTime = booking.StartTime,
                                        EndTime = booking.EndTime,
                                        CarModel = booking.Car?.Model
                                    });
                                }
                            }
                            currentDate = currentDate.AddDays(7);
                        }
                    }
                }



                calendarBookings = calendarBookings.OrderBy(b => b.BookingDate).ToList();
                return Ok(calendarBookings);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error: {ex.Message}");
                return StatusCode(500, "An error occurred while processing your request.");
            }
        }
        private BookingCalendarDto MapToCalendarDto(Booking booking)
        {
            return new BookingCalendarDto
            {
                BookingDate = booking.BookingDate,
                StartTime = booking.StartTime,
                EndTime = booking.EndTime,
                CarModel = booking.Car.Model
            };
        }





        [HttpPost("Booking")]
        public async Task<ActionResult<CreateUpdateBookingDto>> PostBooking(CreateUpdateBookingDto booking)
        {
            if (booking.BookingDate < DateOnly.FromDateTime(DateTime.Today))
            {
                return BadRequest("You cannot book a car for a past date.");
            }

            var car = await _context.Cars.FindAsync(booking.CarId);
            if (car == null)
            {
                return BadRequest("The selected car does not exist.");
            }

            var currentDate = booking.BookingDate;
            var endDate = booking.EndRepeatDate ?? booking.BookingDate;
            var conflictDates = new List<DateOnly>();

            while (currentDate <= endDate)
            {
                bool isDuplicate = await _context.Bookings.AnyAsync(b =>
                    b.CarId == booking.CarId &&
                    b.BookingDate == currentDate &&
                    b.StartTime < booking.EndTime && b.EndTime > booking.StartTime
                );

                if (isDuplicate)
                {
                    conflictDates.Add(currentDate);
                }

                currentDate = booking.RepeatOption == RepeatOption.Weekly ? currentDate.AddDays(7) : currentDate.AddDays(1);
            }

            if (conflictDates.Any())
            {
                return BadRequest($"This car is already booked for the same date and time: {string.Join(", ", conflictDates)}");
            }

            var bookingsToAdd = new List<Booking>();
            currentDate = booking.BookingDate;

            while (currentDate <= endDate)
            {
                bookingsToAdd.Add(new Booking
                {
                    Id = Guid.NewGuid(),
                    BookingDate = currentDate,
                    StartTime = booking.StartTime,
                    EndTime = booking.EndTime,
                    RepeatOption = booking.RepeatOption,
                    EndRepeatDate = booking.EndRepeatDate,
                    DaysToRepeatOn = booking.DaysToRepeatOn,
                    RequestedOn = DateTime.Now,
                    CarId = booking.CarId,
                    Car = car
                });

                currentDate = booking.RepeatOption == RepeatOption.Weekly ? currentDate.AddDays(7) : currentDate.AddDays(1);
            }

            _context.Bookings.AddRange(bookingsToAdd);
            await _context.SaveChangesAsync();

            return Ok(booking);
        }




        // GET: api/SeedData
        // For test purpose
        [HttpGet("SeedData")]
        public async Task<IEnumerable<BookingCalendarDto>> GetSeedData()
        {
            var cars = await _context.Cars.ToListAsync();

            if (!cars.Any())
            {
                cars = GetCars().ToList();
                await _context.Cars.AddRangeAsync(cars);
                await _context.SaveChangesAsync();
            }

            var bookings = await _context.Bookings.ToListAsync();

            if(!bookings.Any())
            {
                bookings = GetBookings().ToList();

                await _context.Bookings.AddRangeAsync(bookings);
                await _context.SaveChangesAsync();
            }

            var calendar = new Dictionary<DateOnly, List<Booking>>();

            foreach (var booking in bookings)
            {
                var currentDate = booking.BookingDate;
                while (currentDate <= (booking.EndRepeatDate ?? booking.BookingDate))
                {
                    if (!calendar.ContainsKey(currentDate))
                        calendar[currentDate] = new List<Booking>();

                    calendar[currentDate].Add(booking);

                    currentDate = booking.RepeatOption switch
                    {
                        RepeatOption.Daily => currentDate.AddDays(1),
                        RepeatOption.Weekly => currentDate.AddDays(7),
                        _ => booking.EndRepeatDate.HasValue ? booking.EndRepeatDate.Value.AddDays(1) : currentDate.AddDays(1)
                    };
                }
            }

            List<BookingCalendarDto> result = new List<BookingCalendarDto>();

            foreach (var item in calendar)
            {
                foreach(var booking in item.Value)
                {
                    result.Add(new BookingCalendarDto { BookingDate = booking.BookingDate, CarModel = booking.Car.Model, StartTime = booking.StartTime, EndTime = booking.EndTime });
                }
            }

            return result;
        }

        #region Sample Data

        private IList<Car> GetCars()
        {
            var cars = new List<Car>
            {
                new Car { Id = Guid.NewGuid(), Make = "Toyota", Model = "Corolla" },
                new Car { Id = Guid.NewGuid(), Make = "Honda", Model = "Civic" },
                new Car { Id = Guid.NewGuid(), Make = "Ford", Model = "Focus" }
            };

            return cars;
        }

        private IList<Booking> GetBookings()
        {
            var cars = GetCars();

            var bookings = new List<Booking>
            {
                new Booking { Id = Guid.NewGuid(), BookingDate = new DateOnly(2025, 2, 5), StartTime = new TimeSpan(10, 0, 0), EndTime = new TimeSpan(12, 0, 0), RepeatOption = RepeatOption.DoesNotRepeat, RequestedOn = DateTime.Now, CarId = cars[0].Id, Car = cars[0] },
                new Booking { Id = Guid.NewGuid(), BookingDate = new DateOnly(2025, 2, 10), StartTime = new TimeSpan(14, 0, 0), EndTime = new TimeSpan(16, 0, 0), RepeatOption = RepeatOption.Daily, EndRepeatDate = new DateOnly(2025, 2, 20), RequestedOn = DateTime.Now, CarId = cars[1].Id, Car = cars[1] },
                new Booking { Id = Guid.NewGuid(), BookingDate = new DateOnly(2025, 2, 15), StartTime = new TimeSpan(9, 0, 0), EndTime = new TimeSpan(10, 30, 0), RepeatOption = RepeatOption.Weekly, EndRepeatDate = new DateOnly(2025, 3, 31), RequestedOn = DateTime.Now, DaysToRepeatOn = DaysOfWeek.Monday, CarId = cars[2].Id,  Car = cars[2] },
                new Booking { Id = Guid.NewGuid(), BookingDate = new DateOnly(2025, 3, 1), StartTime = new TimeSpan(11, 0, 0), EndTime = new TimeSpan(13, 0, 0), RepeatOption = RepeatOption.DoesNotRepeat, RequestedOn = DateTime.Now, CarId = cars[0].Id, Car = cars[0] },
                new Booking { Id = Guid.NewGuid(), BookingDate = new DateOnly(2025, 3, 7), StartTime = new TimeSpan(8, 0, 0), EndTime = new TimeSpan(10, 0, 0), RepeatOption = RepeatOption.Weekly, EndRepeatDate = new DateOnly(2025, 3, 28), RequestedOn = DateTime.Now, DaysToRepeatOn = DaysOfWeek.Friday, CarId = cars[1].Id, Car = cars[1] },
                new Booking { Id = Guid.NewGuid(), BookingDate = new DateOnly(2025, 3, 15), StartTime = new TimeSpan(15, 0, 0), EndTime = new TimeSpan(17, 0, 0), RepeatOption = RepeatOption.Daily, EndRepeatDate = new DateOnly(2025, 3, 20), RequestedOn = DateTime.Now, CarId = cars[2].Id,  Car = cars[2] }
            };

            return bookings;
        }

            #endregion

        }
}
