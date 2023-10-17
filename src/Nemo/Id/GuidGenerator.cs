﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Nemo.Id;

public class GuidGenerator : IIdGenerator
{
    public object Generate()
    {
        return Guid.NewGuid();
    }
}

public class GuidStringGenerator : IIdGenerator
{
    public object Generate()
    {
        return Guid.NewGuid().ToString("N");
    }
}
