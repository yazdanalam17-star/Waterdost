namespace WaterApp.Domain.Enums;

public enum UserRole
{
    Buyer = 0,
    Seller = 1,
    Admin = 2
}

public enum SellerStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Suspended = 3
}

public enum OrderStatus
{
    Placed = 0,
    Confirmed = 1,
    OutForDelivery = 2,
    Delivered = 3,
    Cancelled = 4,
    PendingPayment = 5
}

public enum PaymentMode
{
    Online = 0,
    CashOnDelivery = 1
}

public enum PaymentStatus
{
    Pending = 0,
    Success = 1,
    Failed = 2,
    Refunded = 3,
    CollectedInCash = 4
}
