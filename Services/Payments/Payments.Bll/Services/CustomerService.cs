using AutoMapper;
using Microsoft.Extensions.Logging;
using Payments.Bll.Common;
using Payments.Bll.Dto;
using Payments.Dal.Interfaces;
using Payments.Domain.Exceptions;
using Payments.Domain.Models;

namespace Payments.Bll.Services;

public sealed class CustomerService : ICustomerService
{
    private const string Actor = "api";

    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;
    private readonly ILogger<CustomerService> _logger;

    public CustomerService(IUnitOfWork uow, IMapper mapper, ILogger<CustomerService> logger)
    {
        _uow = uow;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<CustomerDto> GetAsync(long id, CancellationToken ct = default)
    {
        var customer = await _uow.Customers.GetByIdAsync(id, ct)
                       ?? throw new NotFoundException($"Customer {id} was not found.");
        return _mapper.Map<CustomerDto>(customer);
    }

    public async Task<IReadOnlyList<CustomerDto>> ListAsync(int page, int pageSize, CancellationToken ct = default)
    {
        Pagination.Validate(page, pageSize);
        var rows = await _uow.Customers.GetPageAsync((page - 1) * pageSize, pageSize, ct);
        return _mapper.Map<List<CustomerDto>>(rows);
    }

    public Task<CustomerDto> CreateAsync(CreateCustomerDto dto, CancellationToken ct = default) =>
        _uow.InTransactionAsync(async () =>
        {
            // бізнес-правило (другий рівень валідації): e-mail унікальний серед активних платників
            var email = dto.Email.Trim();
            if (await _uow.Customers.GetByEmailAsync(email, ct) is not null)
                throw new BusinessConflictException($"Customer with e-mail '{email}' already exists.");

            var customer = _mapper.Map<Customer>(dto);
            customer.Email = email;
            customer.FullName = dto.FullName.Trim();
            customer.CreatedBy = Actor;

            var id = await _uow.Customers.AddAsync(customer, ct);
            _logger.LogInformation("Customer {CustomerId} created", id);

            var created = await _uow.Customers.GetByIdAsync(id, ct)
                          ?? throw new NotFoundException($"Customer {id} was not found.");
            return _mapper.Map<CustomerDto>(created);
        }, _logger, ct: ct);

    public Task<CustomerDto> UpdateAsync(long id, UpdateCustomerDto dto, CancellationToken ct = default) =>
        _uow.InTransactionAsync(async () =>
        {
            var customer = await _uow.Customers.GetByIdAsync(id, ct)
                           ?? throw new NotFoundException($"Customer {id} was not found.");

            var email = dto.Email.Trim();
            var other = await _uow.Customers.GetByEmailAsync(email, ct);
            if (other is not null && other.Id != id)
                throw new BusinessConflictException($"Customer with e-mail '{email}' already exists.");

            customer.Email = email;
            customer.FullName = dto.FullName.Trim();
            customer.UpdatedBy = Actor;
            await _uow.Customers.UpdateAsync(customer, ct);

            var updated = await _uow.Customers.GetByIdAsync(id, ct)
                          ?? throw new NotFoundException($"Customer {id} was not found.");
            return _mapper.Map<CustomerDto>(updated);
        }, _logger, ct: ct);

    public Task DeleteAsync(long id, CancellationToken ct = default) =>
        _uow.InTransactionAsync(async () =>
        {
            if (!await _uow.Customers.SoftDeleteAsync(id, Actor, ct))
                throw new NotFoundException($"Customer {id} was not found.");
            _logger.LogInformation("Customer {CustomerId} soft-deleted", id);
            return true;
        }, _logger, ct: ct);
}

internal static class Pagination
{
    public static void Validate(int page, int pageSize)
    {
        if (page < 1) throw new ValidationException("Page must be 1 or greater.");
        if (pageSize is < 1 or > 100) throw new ValidationException("Page size must be between 1 and 100.");
    }
}
