using System.ComponentModel.DataAnnotations;

namespace Payments.Bll.Dto;

/// <summary>Платник у відповідях API (без службових полів IsDeleted/CreatedBy).</summary>
public class CustomerDto
{
    public long Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Вхідна модель створення платника. Перший рівень валідації — DataAnnotations.</summary>
public class CreateCustomerDto
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required, StringLength(200, MinimumLength = 2)]
    public string FullName { get; init; } = string.Empty;
}

public class UpdateCustomerDto
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required, StringLength(200, MinimumLength = 2)]
    public string FullName { get; init; } = string.Empty;
}
