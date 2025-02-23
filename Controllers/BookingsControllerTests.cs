using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Wafi.SampleTest.Controllers;
using Wafi.SampleTest.Dtos;
using Wafi.SampleTest.Entities;
using Xunit;

namespace Wafi.SampleTest.Tests
{
    public class BookingsControllerTests
    {
        private BookingsController GetControllerWithContext(out WafiDbContext context)
        {
            var options = new DbContextOptionsBuilder<WafiDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            context = new WafiDbContext(options);
            return new BookingsController(context);
        }

        [Fact]
        public async Task GetCalendarBookings_ShouldReturnBadRequest_WhenStartDateIsAfterEndDate()
        {
            var controller = GetControllerWithContext(out var context);
            var input = new BookingFilterDto
            {
                CarId = Guid.NewGuid(),
                StartBookingDate = DateOnly.FromDateTime(DateTime.Today.AddDays(5)),
                EndBookingDate = DateOnly.FromDateTime(DateTime.Today.AddDays(3))
            };

            var result = await controller.GetCalendarBookings(input);

            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.Equal("Start date cannot be after end date.", badRequestResult.Value);
        }

        [Fact]
        public async Task GetCalendarBookings_ShouldReturnBookings_WhenValidInput()
        {
            var controller = GetControllerWithContext(out var context);

            var car = new Car { Id = Guid.NewGuid(), Make = "Honda", Model = "Civic" };
            var bookingDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
            var booking = new Booking
            {
                Id = Guid.NewGuid(),
                CarId = car.Id,
                Car = car,
                BookingDate = bookingDate,
                StartTime = new TimeSpan(14, 0, 0),
                EndTime = new TimeSpan(16, 0, 0),
                RepeatOption = RepeatOption.DoesNotRepeat,
                EndRepeatDate = null
            };

            context.Cars.Add(car);
            context.Bookings.Add(booking);
            await context.SaveChangesAsync();

            var input = new BookingFilterDto
            {
                CarId = car.Id,
                StartBookingDate = bookingDate.AddDays(-1),
                EndBookingDate = bookingDate.AddDays(1)
            };

            var result = await controller.GetCalendarBookings(input);

            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var bookings = Assert.IsAssignableFrom<List<BookingCalendarDto>>(okResult.Value);
            Assert.Single(bookings);
        }

        [Fact]
        public async Task PostBooking_ShouldReturnBadRequest_WhenCarDoesNotExist()
        {
            var controller = GetControllerWithContext(out var context);
            var futureDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
            var booking = new CreateUpdateBookingDto
            {
                CarId = Guid.NewGuid(),
                BookingDate = futureDate,
                StartTime = new TimeSpan(14, 0, 0),
                EndTime = new TimeSpan(16, 0, 0),
                RepeatOption = RepeatOption.DoesNotRepeat
            };

            var result = await controller.PostBooking(booking);

            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.Equal("The selected car does not exist.", badRequestResult.Value);
        }

        [Fact]
        public async Task PostBooking_ShouldReturnBadRequest_WhenBookingTimeConflicts()
        {
            var controller = GetControllerWithContext(out var context);
            var futureDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
            var car = new Car { Id = Guid.NewGuid(), Make = "Honda", Model = "Civic" };

            var existingBooking = new Booking
            {
                Id = Guid.NewGuid(),
                CarId = car.Id,
                Car = car,
                BookingDate = futureDate,
                StartTime = new TimeSpan(14, 0, 0),
                EndTime = new TimeSpan(16, 0, 0),
                RepeatOption = RepeatOption.DoesNotRepeat
            };

            context.Cars.Add(car);
            context.Bookings.Add(existingBooking);
            await context.SaveChangesAsync();

            var booking = new CreateUpdateBookingDto
            {
                CarId = car.Id,
                BookingDate = futureDate,
                StartTime = new TimeSpan(15, 0, 0),  
                EndTime = new TimeSpan(17, 0, 0),
                RepeatOption = RepeatOption.DoesNotRepeat
            };

            var result = await controller.PostBooking(booking);

            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.StartsWith("This car is already booked for the same date and time:", badRequestResult.Value.ToString());
        }

        [Fact]
        public async Task PostBooking_ShouldAddBooking_WhenValid()
        {
            var controller = GetControllerWithContext(out var context);
            var futureDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
            var car = new Car { Id = Guid.NewGuid(), Make = "Honda", Model = "Civic" };
            context.Cars.Add(car);
            await context.SaveChangesAsync();

            var booking = new CreateUpdateBookingDto
            {
                CarId = car.Id,
                BookingDate = futureDate,
                StartTime = new TimeSpan(14, 0, 0),
                EndTime = new TimeSpan(16, 0, 0),
                RepeatOption = RepeatOption.DoesNotRepeat
            };

            var result = await controller.PostBooking(booking);

            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var returnedBooking = Assert.IsAssignableFrom<CreateUpdateBookingDto>(okResult.Value);
            Assert.Equal(booking.CarId, returnedBooking.CarId);
        }
    }
}
