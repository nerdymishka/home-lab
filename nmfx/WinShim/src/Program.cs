using System;

namespace WinShim
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            System.Console.WriteLine($"Hello World! {string.Join(' ', args)}");
        }
    }
}
