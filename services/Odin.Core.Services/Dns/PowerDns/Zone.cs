using System.Collections.Generic;

public class Zone
{
    public string account { get; set; }
    public bool dnssec { get; set; }
    public int edited_serial { get; set; }
    public string id { get; set; }
    public string kind { get; set; }
    public int last_check { get; set; }
    public List<object> masters { get; set; }
    public string name { get; set; }
    public int notified_serial { get; set; }
    public int serial { get; set; }
    public string url { get; set; }
}