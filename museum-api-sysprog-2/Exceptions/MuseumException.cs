namespace museum_api_sysprog_1.Exceptions
{
    public class MuseumException:Exception
    {
        public int StatusCode{get;set;}

        public string? ApiMessage{get;set;}

        public MuseumException(string message,int statusCode,string? apiMessage=null)
                               :base(message)
        {
            StatusCode=statusCode;
            ApiMessage=apiMessage;
            
        }
    }

}