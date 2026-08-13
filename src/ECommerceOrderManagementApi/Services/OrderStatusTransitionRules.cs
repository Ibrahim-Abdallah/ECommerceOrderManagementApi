using ECommerceOrderManagementApi.Enums;

namespace ECommerceOrderManagementApi.Services;

public static class OrderStatusTransitionRules
{
    public static bool CanAdminTransition(OrderStatus current, OrderStatus requested) =>
        (current, requested) is
            (OrderStatus.Pending, OrderStatus.Confirmed) or
            (OrderStatus.Confirmed, OrderStatus.Shipped) or
            (OrderStatus.Shipped, OrderStatus.Delivered);

    public static bool CanCustomerCancel(OrderStatus current) => current == OrderStatus.Pending;
}
