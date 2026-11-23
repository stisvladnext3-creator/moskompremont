# JSONC comments and trailing commas, respecting quoted strings and escapes.
# Deliberately not a JSON parser: jq validates the result afterwards.
BEGIN { text=""; block=0; quoted=0; escape=0 }
{
 line=$0 "\n"
 if(NR==1)sub(/^\357\273\277/, "", line)
 for(i=1;i<=length(line);i++){
  c=substr(line,i,1); n=substr(line,i+1,1)
  if(block){if(c=="*"&&n=="/"){block=0;i++}else if(c=="\n")text=text c;continue}
  if(quoted){text=text c;if(escape)escape=0;else if(c=="\\")escape=1;else if(c=="\"")quoted=0;continue}
  if(c=="\""){quoted=1;text=text c;continue}
  if(c=="/"&&n=="/")break
  if(c=="/"&&n=="*"){block=1;text=text " ";i++;continue}
  text=text c
 }
 text=text "\n"
}
END {
 if(block||quoted)exit 1
 quoted=0;escape=0;pending=""
 for(i=1;i<=length(text);i++){
  c=substr(text,i,1)
  if(quoted){printf "%s",c;if(escape)escape=0;else if(c=="\\")escape=1;else if(c=="\"")quoted=0;continue}
  if(pending!=""){
   if(c~/[ \t\r\n]/){pending=pending c;continue}
   if(c!="}"&&c!="]")printf "%s",pending
   pending=""
  }
  if(c==","){pending=c;continue}
  if(c=="\"")quoted=1
  printf "%s",c
 }
 if(pending!="")printf "%s",pending
 printf "\n"
}
