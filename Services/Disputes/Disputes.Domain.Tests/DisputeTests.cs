using Disputes.Domain.Entities;
using Disputes.Domain.Enums;
using Disputes.Domain.Exceptions;
using Disputes.Domain.ValueObjects;
using Xunit;

namespace Disputes.Domain.Tests;

public class DisputeTests
{
    private static readonly Author Olena = new("u-1", "Олена");

    private static Dispute NewDispute(decimal amount = 100m, string currency = "UAH")
        => new(paymentId: 1, invoiceId: 1, merchantId: 1, merchantName: "ЕнергоСвіт",
               amount, currency, DisputeReason.Duplicate, Olena);

    // Контрольна точка Лаб. 04: некоректні дані кидають DomainException без жодного звернення до БД.
    [Fact]
    public void Create_with_zero_amount_throws_DomainException()
        => Assert.Throws<DomainException>(() => NewDispute(amount: 0m));

    [Fact]
    public void Create_with_negative_amount_throws_DomainException()
        => Assert.Throws<DomainException>(() => NewDispute(amount: -5m));

    [Fact]
    public void Create_with_unknown_currency_throws_DomainException()
        => Assert.Throws<DomainException>(() => NewDispute(currency: "XXX"));

    [Fact]
    public void Create_with_empty_merchant_name_throws_DomainException()
        => Assert.Throws<DomainException>(() =>
            new Dispute(1, 1, 1, " ", 100m, "UAH", DisputeReason.Fraud, Olena));

    [Fact]
    public void New_dispute_is_open_and_has_no_resolution_date()
    {
        var dispute = NewDispute();

        Assert.Equal(DisputeStatus.Open, dispute.Status);
        Assert.Null(dispute.ResolvedAt);
        Assert.Equal(0, dispute.Version);
    }

    [Fact]
    public void Status_follows_allowed_transitions()
    {
        var dispute = NewDispute();

        dispute.ChangeStatus(DisputeStatus.UnderReview);
        dispute.ChangeStatus(DisputeStatus.Won);
        dispute.ChangeStatus(DisputeStatus.Closed);

        Assert.Equal(DisputeStatus.Closed, dispute.Status);
        Assert.NotNull(dispute.ResolvedAt);
    }

    [Fact]
    public void Transition_back_to_open_is_rejected()
    {
        var dispute = NewDispute();
        dispute.ChangeStatus(DisputeStatus.UnderReview);

        Assert.Throws<DomainException>(() => dispute.ChangeStatus(DisputeStatus.Open));
    }

    [Fact]
    public void Transition_to_the_same_status_is_rejected()
        => Assert.Throws<DomainException>(() => NewDispute().ChangeStatus(DisputeStatus.Open));

    [Fact]
    public void Dispute_under_review_cannot_be_deleted()
    {
        var dispute = NewDispute();
        dispute.ChangeStatus(DisputeStatus.UnderReview);

        Assert.Throws<DomainException>(dispute.EnsureCanBeDeleted);
    }

    [Fact]
    public void Open_dispute_can_be_deleted()
        => NewDispute().EnsureCanBeDeleted();

    [Fact]
    public void AddComment_stores_nested_comment_and_touches_entity()
    {
        var dispute = NewDispute();

        dispute.AddComment("Олена", "Додаю підтвердження оплати");

        var comment = Assert.Single(dispute.Comments);
        Assert.Equal("Додаю підтвердження оплати", comment.Text);
        Assert.NotNull(dispute.UpdatedAt);
    }

    [Fact]
    public void Closed_dispute_does_not_accept_comments()
    {
        var dispute = NewDispute();
        dispute.ChangeStatus(DisputeStatus.Closed);

        Assert.Throws<DomainException>(() => dispute.AddComment("Олена", "Запізно"));
    }

    [Fact]
    public void Comments_collection_is_read_only_from_outside()
    {
        var dispute = NewDispute();

        Assert.IsNotType<List<Comment>>(dispute.Comments);
    }
}

public class ValueObjectTests
{
    [Fact]
    public void Authors_with_same_values_are_equal()
    {
        var a = new Author("u-1", "Олена");
        var b = new Author("u-1", "Олена");

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Authors_with_different_values_are_not_equal()
        => Assert.NotEqual(new Author("u-1", "Олена"), new Author("u-2", "Олена"));

    [Theory]
    [InlineData("", "Олена")]
    [InlineData("u-1", " ")]
    public void Author_requires_user_id_and_display_name(string userId, string displayName)
        => Assert.Throws<DomainException>(() => new Author(userId, displayName));

    [Fact]
    public void Comment_requires_text()
        => Assert.Throws<DomainException>(() => new Comment("Олена", "  ", DateTime.UtcNow));
}
