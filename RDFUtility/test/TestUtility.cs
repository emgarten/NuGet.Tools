﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Emgarten.Test
{
    public static class TestUtility
    {
        public static Stream GetResource(string name)
        {
            var path = $"Emgarten.Test.compiler.resources.{name}";
            return typeof(TestUtility).GetTypeInfo().Assembly.GetManifestResourceStream(path);
        }
    }
}
