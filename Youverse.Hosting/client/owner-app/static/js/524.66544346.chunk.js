"use strict";(self.webpackChunkowner_app=self.webpackChunkowner_app||[]).push([[524],{1524:function(e,t,n){n.r(t);var r=n(4165),a=n(5861),o=n(885),s=n(6117),i=n(2420),c=n(2791),p=n(3504),l=n(7165),u=n(1803),d=n(9779),f=n.n(d),h=n(3689),y=n.n(h),v=n(184);t.default=function(){var e=(0,s.Z)().getSharedSecret,t=(0,p.lr)(),n=(0,o.Z)(t,1)[0],d=(0,c.useState)(),h=(0,o.Z)(d,2),g=h[0],m=h[1],x=(0,c.useState)(),_=(0,o.Z)(x,2),b=_[0],j=_[1],S=(0,c.useState)(null),C=(0,o.Z)(S,2),w=C[0],Z=C[1],N=(0,c.useState)(null),k=(0,o.Z)(N,2),O=k[0],R=k[1],A=(0,c.useState)(null),P=(0,o.Z)(A,2),I=P[0],E=P[1],q=(0,c.useState)(),J=(0,o.Z)(q,2),D=J[0],T=J[1],$=new l.Z(e()),U=new u.l$({api:u.Ii.Owner,sharedSecret:e()}),B={drive:{alias:u.j4.toByteArrayId("chatr-drive"),type:u.j4.toByteArrayId("chat-data")},permission:i.n.Read|i.n.Write},L={permissions:i.r.ReadConnectionRequests|i.r.ReadConnections};(0,c.useEffect)((function(){var e=function(){var e=(0,a.Z)((0,r.Z)().mark((function e(){var t,a,o;return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return t={appId:u.j4.toByteArrayId("chat-r-box"),name:"Chatrbox",permissionSet:L,drives:[B]},a={appId:t.appId,clientPublicKey64:n.get("pk"),clientFriendlyName:n.get("fn")},e.next=4,$.GetAppRegistration({appId:t.appId});case 4:o=e.sent,E(o),Z(t),R(a),T(n.get("rs")),m(!0);case 10:case"end":return e.stop()}}),e)})));return function(){return e.apply(this,arguments)}}();e()}),[n]);var M=function(){var e=(0,a.Z)((0,r.Z)().mark((function e(){return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return e.next=2,U.EnsureDrive(B.drive,"Chat Drive","Chatr drive",!1);case 2:return e.next=4,$.RegisterApp(w);case 4:return e.next=6,W();case 6:case"end":return e.stop()}}),e)})));return function(){return e.apply(this,arguments)}}(),W=function(){var e=(0,a.Z)((0,r.Z)().mark((function e(){var t,n,a,o;return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return e.next=2,$.RegisterChatAppClient_temp(O);case 2:t=e.sent,n=encodeURI(t.data),a=u.j4.base64ToUint8Array(n),console.log("bytes",a),o="".concat(D,"?d=").concat(n,"&v=").concat(t.encryptionVersion),j(o);case 8:case"end":return e.stop()}}),e)})));return function(){return e.apply(this,arguments)}}();return g?b?(0,v.jsxs)("div",{className:"container mx-auto",children:["You have approved the app and this client...",(0,v.jsx)("br",{}),(0,v.jsx)("br",{}),(0,v.jsx)("a",{href:b,children:"Click to continue..."}),(0,v.jsx)("br",{})]}):(0,v.jsx)("section",{className:"mt-20",children:(0,v.jsx)("div",{className:"container mx-auto",children:(0,v.jsxs)("div",{className:"-m-5 flex flex-wrap",children:[g&&null==I&&(0,v.jsxs)("div",{className:"row",children:[(0,v.jsxs)("div",{className:"px-5",children:["App with id ",w.appId," is not registered.",(0,v.jsx)("br",{}),"Clicking OK will approve the app for use and register this client"]}),(0,v.jsx)("div",{className:"px-5",children:(0,v.jsx)("a",{onClick:M,className:"mt-10 block rounded border-0 bg-green-500 py-2 px-4 text-white hover:bg-green-600 focus:outline-none",children:"Register Now"})})]}),g&&null!=I&&(0,v.jsxs)("div",{className:"row",children:[(0,v.jsxs)("div",{className:"px-5",children:[(0,v.jsxs)("div",{className:"pb-2",children:["The App '",null===I||void 0===I?void 0:I.name,"' is registered"]}),(0,v.jsx)(f(),{id:"json-pretty",theme:y(),data:u.j4.JsonStringify64(I)})]}),(0,v.jsx)("div",{className:"px-5",children:(0,v.jsx)("a",{onClick:W,className:"mt-10 block rounded border-0 bg-green-500 py-2 px-4 text-white hover:bg-green-600 focus:outline-none",children:"Register this Client"})})]})]})})}):(0,v.jsx)("div",{children:"Loading..."})}},7165:function(e,t,n){n.d(t,{Z:function(){return p}});var r=n(4165),a=n(5861),o=n(5671),s=n(3144),i=n(9340),c=n(1129),p=function(e){(0,i.Z)(n,e);var t=(0,c.Z)(n);function n(e){return(0,o.Z)(this,n),t.call(this,e)}return(0,s.Z)(n,[{key:"RegisterAppClient",value:function(){var e=(0,a.Z)((0,r.Z)().mark((function e(t){var n,a;return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return n=this.createAxiosClient(),e.next=3,n.post("appmanagement/register/client",t);case 3:return a=e.sent,e.abrupt("return",a.data);case 5:case"end":return e.stop()}}),e,this)})));return function(t){return e.apply(this,arguments)}}()},{key:"RegisterChatAppClient_temp",value:function(){var e=(0,a.Z)((0,r.Z)().mark((function e(t){var n,a;return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return n=this.createAxiosClient(),e.next=3,n.post("appmanagement/register/chatclient_temp",t);case 3:return a=e.sent,console.log("RegisterChatAppClient_temp returning response"),console.log(a),e.abrupt("return",a.data);case 7:case"end":return e.stop()}}),e,this)})));return function(t){return e.apply(this,arguments)}}()},{key:"RegisterApp",value:function(){var e=(0,a.Z)((0,r.Z)().mark((function e(t){var n,a;return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return n=this.createAxiosClient(),e.next=3,n.post("appmanagement/register/app",t);case 3:return a=e.sent,console.log("RegisterApp returning response"),console.log(a),e.abrupt("return",a.data);case 7:case"end":return e.stop()}}),e,this)})));return function(t){return e.apply(this,arguments)}}()},{key:"GetAppRegistration",value:function(){var e=(0,a.Z)((0,r.Z)().mark((function e(t){var n,a;return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return n=this.createAxiosClient(),e.next=3,n.post("appmanagement/app",t);case 3:return a=e.sent,e.abrupt("return",a.data);case 5:case"end":return e.stop()}}),e,this)})));return function(t){return e.apply(this,arguments)}}()}]),n}(n(2008).$)},2420:function(e,t,n){var r,a;n.d(t,{n:function(){return r},r:function(){return a}}),function(e){e[e.None=0]="None",e[e.Read=1]="Read",e[e.Write=4]="Write"}(r||(r={})),function(e){e[e.None=0]="None",e[e.ApproveConnection=1]="ApproveConnection",e[e.ReadConnections=2]="ReadConnections",e[e.UpdateConnections=4]="UpdateConnections",e[e.DeleteConnections=8]="DeleteConnections",e[e.CreateOrSendConnectionRequests=16]="CreateOrSendConnectionRequests",e[e.ReadConnectionRequests=32]="ReadConnectionRequests",e[e.DeleteConnectionRequests=64]="DeleteConnectionRequests"}(a||(a={}))},9779:function(e,t,n){var r=this&&this.__extends||function(){var e=function(t,n){return e=Object.setPrototypeOf||{__proto__:[]}instanceof Array&&function(e,t){e.__proto__=t}||function(e,t){for(var n in t)t.hasOwnProperty(n)&&(e[n]=t[n])},e(t,n)};return function(t,n){function r(){this.constructor=t}e(t,n),t.prototype=null===n?Object.create(n):(r.prototype=n.prototype,new r)}}(),a=this&&this.__assign||function(){return a=Object.assign||function(e){for(var t,n=1,r=arguments.length;n<r;n++)for(var a in t=arguments[n])Object.prototype.hasOwnProperty.call(t,a)&&(e[a]=t[a]);return e},a.apply(this,arguments)},o=this&&this.__rest||function(e,t){var n={};for(var r in e)Object.prototype.hasOwnProperty.call(e,r)&&t.indexOf(r)<0&&(n[r]=e[r]);if(null!=e&&"function"===typeof Object.getOwnPropertySymbols){var a=0;for(r=Object.getOwnPropertySymbols(e);a<r.length;a++)t.indexOf(r[a])<0&&(n[r[a]]=e[r[a]])}return n},s=this&&this.__importStar||function(e){if(e&&e.__esModule)return e;var t={};if(null!=e)for(var n in e)Object.hasOwnProperty.call(e,n)&&(t[n]=e[n]);return t.default=e,t},i=s(n(2007)),c=s(n(2791));function p(e,t,n){var r=function(e,t,n){var r=n[e+"Style"]||"",a=t&&t[e]||"";return r?r+";"+a:a}(e,t,n);return r?' style="'+r+'"':""}var l={'"':"&quot;","'":"&apos;","&":"&amp;",">":"&gt;","<":"&lt"};var u=function(e){function t(){return null!==e&&e.apply(this,arguments)||this}return r(t,e),t.prototype.render=function(){var e,t=this.props,n=t.json,r=t.data,s=t.replacer,i=t.space,u=t.themeClassName,d=t.theme,f=t.onJSONPrettyError,h=t.onError,y=t.silent,v=t.mainStyle,g=t.keyStyle,m=t.valueStyle,x=t.stringStyle,_=t.booleanStyle,b=t.errorStyle,j=o(t,["json","data","replacer","space","themeClassName","theme","onJSONPrettyError","onError","silent","mainStyle","keyStyle","valueStyle","stringStyle","booleanStyle","errorStyle"]),S={mainStyle:v,keyStyle:g,valueStyle:m,stringStyle:x,booleanStyle:_,errorStyle:b},C=r||n;if("string"===typeof C)try{C=JSON.parse(C)}catch(w){return y||console.warn("[react-json-pretty]: "+w.message),f&&f(w),!f&&h&&(h(w),console.warn("JSONPretty#onError is deprecated, please use JSONPretty#onJSONPrettyError instead")),c.createElement("div",a({},j,{dangerouslySetInnerHTML:{__html:'<pre class="__json-pretty-error__"'+p("error",d,S)+">"+(e=C,(e?e.replace(/<|>|&|"|'/g,(function(e){return l[e]})):e)+"</pre>")}}))}return c.createElement("div",a({},j,{dangerouslySetInnerHTML:{__html:'<pre class="'+u+'"'+p("main",d,S)+">"+this._pretty(d,C,s,+i,S)+"</pre>"}}))},t.prototype._pretty=function(e,t,n,r,a){var o=JSON.stringify(t,"function"===typeof n?n:null,isNaN(r)?2:r);return o?o.replace(/&/g,"&amp;").replace(/\\"([^,])/g,"\\&quot;$1").replace(/</g,"&lt;").replace(/>/g,"&gt;").replace(/^( *)("[^"]+": )?("[^"]*"|[\w.+-]*)?([,[{]|\[\s*\],?|\{\s*\},?)?$/gm,this._replace.bind(null,e,a)):o},t.prototype._replace=function(e,t,n,r,a,o,s){var i="</span>",c='<span class="__json-key__"'+p("key",e,t)+">",l='<span class="__json-value__"'+p("value",e,t)+">",u='<span class="__json-string__"'+p("string",e,t)+">",d='<span class="__json-boolean__"'+p("boolean",e,t)+">",f=r||"";return a&&(f=f+'"'+c+a.replace(/^"|":\s$/g,"")+'</span>": '),o&&(f="true"===o||"false"===o?f+d+o+i:f+('"'===o[0]?u:l)+o+i),f+(s||"")},t.propTypes={data:i.any,json:i.any,replacer:i.func,silent:i.bool,space:i.oneOfType([i.number,i.string]),theme:i.object,themeClassName:i.string,onJSONPrettyError:i.func},t.defaultProps={data:"",json:"",silent:!0,space:2,themeClassName:"__json-pretty__"},t}(c.Component);e.exports=u},3689:function(e){e.exports={main:"line-height:1.3;color:#66d9ef;background:#272822;overflow:auto;",error:"line-height:1.3;color:#66d9ef;background:#272822;overflow:auto;",key:"color:#f92672;",string:"color:#fd971f;",value:"color:#a6e22e;",boolean:"color:#ac81fe;"}}}]);
//# sourceMappingURL=524.66544346.chunk.js.map