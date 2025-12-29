select *
from "Matches" where "GoalsCount">0 and "IsParsed" = true;

select *
from "Matches" where "GoalsCount"=0 and "IsParsed" = true;

select *
from "Matches" where "IsParsed" = false 
               order by "Date";