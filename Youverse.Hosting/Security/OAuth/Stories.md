### DI to DI authentication: Sam to log into frodo's DI on his landing page
1) Sam comes to landingpage.frodobaggins.me and clicks login
2) Oauth validation occurs where frodobaggins.me validates Samwisegamgee.me
   1) if Sam is not  already logged into samwisegamgee.me, he is prompted to login
   2) if Sam has not authorized frodobaggins.me authenticate, then authorization screen is shown
      1) Authorization screen allows sam to choose which information to share (note: this could be a fake profile saying his name is "bob barker")
      2) includes: list of fields to show (name, photo)
         1) TODO: need to determine the data format of fields (i.e. json, something in openid, schema.org, etc?)
   3) oauth protocol continues here.

### 
Research items
- Need to determine if oauth2 supports scopes and claims if so, how does it compare to open id connect?  W
  - We need a method to send claims (i.e. firstname:frodo) to consumers.
  - Determine which is the best route for this?  Oauth, openid, or custom?
    - i.e. names of the fields 
    - how the information is passed to the consumer?
      - does oauth or openid support encryption of information (in addition to SSL) 

****
      
**Authenticate** - in youverse, this is the action of proving that an individual controls a given domain.  It is not possible to authentication without having prior authorization of the 'authenticate' operation  
- for example: when frodo authenticates sam, it means sam is proving to frodobaggins.me that Sam controls samwisegamgee.me

**Authorization**
1) operation: authenticate - in youverse, to authorize the operation of authentication some on means that you are allowed to authenticate 
   - for example:  Sam allows frodo to get proof that Sam controls samwisegamgee.me

2) operation: getdata(picture, name, etc).