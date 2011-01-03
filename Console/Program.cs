using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data.SqlClient;

namespace Consoles
{
    class Program
    {
        static void Main(string[] args)
        {
            

            Test.Dummy dummy = new Test.Dummy("Olle");
            Console.WriteLine(dummy.ToMessage());
            Console.ReadKey();
        }
    }
}
