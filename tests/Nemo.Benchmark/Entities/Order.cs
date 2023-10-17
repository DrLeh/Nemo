﻿using Nemo.Attributes;

namespace Nemo.Benchmark.Entities;

[Table("Orders")]
public class Order
{
    [PrimaryKey]
    public int OrderId
    {
        get;
        set;
    }

    [References(typeof(Customer))]
    public string CustomerId
    {
        get;
        set;
    }

    public Customer Customer { get; set; }

    public string ShipPostalCode { get; set; }
}
