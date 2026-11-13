namespace EventSphere.Domain;

public readonly record struct Money(decimal Amount, string Currency)
{
    public static Money Create(decimal amount, string currency = "BRL")
    {
        if (amount <= 0) throw new DomainRuleViolationException("Money must be greater than zero.");
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
            throw new DomainRuleViolationException("Currency must use a three-letter ISO code.");

        return new Money(decimal.Round(amount, 2, MidpointRounding.ToEven), currency.ToUpperInvariant());
    }
}

public readonly record struct IdempotencyKey
{
    private IdempotencyKey(string value) => Value = value;
    public string Value { get; }

    public static IdempotencyKey Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length is < 8 or > 128)
            throw new DomainRuleViolationException("Idempotency key must contain between 8 and 128 characters.");
        return new IdempotencyKey(value.Trim());
    }

    public override string ToString() => Value;
}

public readonly record struct Seat(string Sector, string Row, string Number)
{
    public static Seat Create(string sector, string row, string number)
    {
        if (string.IsNullOrWhiteSpace(sector) || string.IsNullOrWhiteSpace(row) || string.IsNullOrWhiteSpace(number))
            throw new DomainRuleViolationException("Sector, row and seat number are required.");
        return new Seat(sector.Trim(), row.Trim(), number.Trim());
    }
}
