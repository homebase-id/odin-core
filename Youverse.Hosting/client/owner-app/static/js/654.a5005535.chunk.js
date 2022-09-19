"use strict";(self.webpackChunkowner_app=self.webpackChunkowner_app||[]).push([[654],{4654:function(e,t,n){n.r(t),n.d(t,{default:function(){return g}});var r,a,s=n(4165),o=n(5861),i=n(885),c=n(6117);!function(e){e[e.None=0]="None",e[e.Read=1]="Read",e[e.Write=4]="Write"}(r||(r={})),function(e){e[e.None=0]="None",e[e.ReadConnections=10]="ReadConnections",e[e.ReadConnectionRequests=30]="ReadConnectionRequests",e[e.ReadCircleMembers=50]="ReadCircleMembers"}(a||(a={}));var p=n(2791),u=n(3504),l=n(4180),f=n(1803),d=n(9779),h=n.n(d),y=n(3689),v=n.n(y),m=n(184),g=function(){var e=(0,c.Z)().getSharedSecret,t=(0,u.lr)(),n=(0,i.Z)(t,1)[0],d=(0,p.useState)(),y=(0,i.Z)(d,2),g=y[0],x=y[1],_=(0,p.useState)(),w=(0,i.Z)(_,2),b=w[0],j=w[1],Z=(0,p.useState)(null),S=(0,i.Z)(Z,2),k=S[0],C=S[1],N=(0,p.useState)(null),A=(0,i.Z)(N,2),O=A[0],R=A[1],P=(0,p.useState)(null),E=(0,i.Z)(P,2),I=E[0],J=E[1],B=(0,p.useState)(),T=(0,i.Z)(B,2),q=T[0],M=T[1],$=new l.Z(e()),D=new f.l$({api:f.Ii.Owner,sharedSecret:e()}),G={permissionedDrive:{drive:{alias:f.j4.toByteArrayId("AAA333ace3382940"),type:f.j4.toByteArrayId("BBB444ace3382940")},permission:r.Read|r.Write}},L={keys:[a.ReadConnectionRequests,a.ReadConnections]};(0,p.useEffect)((function(){var e=function(){var e=(0,o.Z)((0,s.Z)().mark((function e(){var t,r,a;return(0,s.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return t={appId:f.j4.toByteArrayId("EEE777ace3382940"),name:"Chatrbox",permissionSet:L,drives:[G]},r={appId:t.appId,clientPublicKey64:n.get("pk"),clientFriendlyName:n.get("fn")},e.next=4,$.GetAppRegistration({appId:t.appId});case 4:a=e.sent,J(a),C(t),R(r),M(n.get("rs")),x(!0);case 10:case"end":return e.stop()}}),e)})));return function(){return e.apply(this,arguments)}}();e()}),[n]);var W=function(){var e=(0,o.Z)((0,s.Z)().mark((function e(){return(0,s.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return e.next=2,D.EnsureDrive(G.permissionedDrive.drive,"Chat Drive","Chatr drive",!1);case 2:return e.next=4,$.RegisterApp(k);case 4:return e.next=6,H();case 6:case"end":return e.stop()}}),e)})));return function(){return e.apply(this,arguments)}}(),H=function(){var e=(0,o.Z)((0,s.Z)().mark((function e(){var t,n,r,a;return(0,s.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return e.next=2,$.RegisterChatAppClient_temp(O);case 2:t=e.sent,n=encodeURI(t.data),r=f.j4.base64ToUint8Array(n),console.log("bytes",r),a="".concat(q,"?d=").concat(n,"&v=").concat(t.encryptionVersion),j(a);case 8:case"end":return e.stop()}}),e)})));return function(){return e.apply(this,arguments)}}();return g?b?(0,m.jsxs)("div",{className:"container mx-auto",children:["You have approved the app...",(0,m.jsx)("br",{}),(0,m.jsx)("br",{}),(0,m.jsx)("a",{href:b,children:"Click to continue..."}),(0,m.jsx)("br",{})]}):(0,m.jsx)("section",{className:"mt-20",children:(0,m.jsx)("div",{className:"container mx-auto",children:(0,m.jsxs)("div",{className:"-m-5 flex flex-wrap",children:[g&&null==I&&(0,m.jsxs)("div",{className:"row",children:[(0,m.jsxs)("div",{className:"px-5",children:["App with id ",k.appId," is not registered.",(0,m.jsx)("br",{}),"Clicking OK will approve the app for use and register this client"]}),(0,m.jsx)("div",{className:"px-5",children:(0,m.jsx)("a",{onClick:W,className:"mt-10 block rounded border-0 bg-green-500 py-2 px-4 text-white hover:bg-green-600 focus:outline-none",children:"Register Now"})})]}),g&&null!=I&&(0,m.jsxs)("div",{className:"row",children:[(0,m.jsxs)("div",{className:"px-5",children:[(0,m.jsxs)("div",{className:"pb-2",children:["The App '",null===I||void 0===I?void 0:I.name,"' is registered"]}),(0,m.jsx)(h(),{id:"json-pretty",theme:v(),data:f.j4.JsonStringify64(I)})]}),(0,m.jsx)("div",{className:"px-5",children:(0,m.jsx)("a",{onClick:H,className:"mt-10 block rounded border-0 bg-green-500 py-2 px-4 text-white hover:bg-green-600 focus:outline-none",children:"Register this Client"})})]})]})})}):(0,m.jsx)("div",{children:"Loading..."})}},4180:function(e,t,n){n.d(t,{Z:function(){return p}});var r=n(4165),a=n(5861),s=n(5671),o=n(3144),i=n(9340),c=n(1129),p=function(e){(0,i.Z)(n,e);var t=(0,c.Z)(n);function n(e){return(0,s.Z)(this,n),t.call(this,e)}return(0,o.Z)(n,[{key:"RegisterAppClient",value:function(){var e=(0,a.Z)((0,r.Z)().mark((function e(t){var n,a;return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return n=this.createAxiosClient(),e.next=3,n.post("appmanagement/register/client",t);case 3:return a=e.sent,e.abrupt("return",a.data);case 5:case"end":return e.stop()}}),e,this)})));return function(t){return e.apply(this,arguments)}}()},{key:"RegisterChatAppClient_temp",value:function(){var e=(0,a.Z)((0,r.Z)().mark((function e(t){var n,a;return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return n=this.createAxiosClient(),e.next=3,n.post("appmanagement/register/chatclient_temp",t);case 3:return a=e.sent,console.log("RegisterChatAppClient_temp returning response"),console.log(a),e.abrupt("return",a.data);case 7:case"end":return e.stop()}}),e,this)})));return function(t){return e.apply(this,arguments)}}()},{key:"RegisterApp",value:function(){var e=(0,a.Z)((0,r.Z)().mark((function e(t){var n,a;return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return n=this.createAxiosClient(),e.next=3,n.post("appmanagement/register/app",t);case 3:return a=e.sent,console.log("RegisterApp returning response"),console.log(a),e.abrupt("return",a.data);case 7:case"end":return e.stop()}}),e,this)})));return function(t){return e.apply(this,arguments)}}()},{key:"GetAppRegistration",value:function(){var e=(0,a.Z)((0,r.Z)().mark((function e(t){var n,a;return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return n=this.createAxiosClient(),e.next=3,n.post("appmanagement/app",t);case 3:return a=e.sent,e.abrupt("return",a.data);case 5:case"end":return e.stop()}}),e,this)})));return function(t){return e.apply(this,arguments)}}()},{key:"GetAppRegistrations",value:function(){var e=(0,a.Z)((0,r.Z)().mark((function e(){var t,n;return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return t=this.createAxiosClient(),e.next=3,t.get("appmanagement/list");case 3:return n=e.sent,e.abrupt("return",n.data);case 5:case"end":return e.stop()}}),e,this)})));return function(){return e.apply(this,arguments)}}()},{key:"RevokeApp",value:function(){var e=(0,a.Z)((0,r.Z)().mark((function e(t){var n;return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return n=this.createAxiosClient(),e.next=3,n.post("appmanagement/revoke",t);case 3:case"end":return e.stop()}}),e,this)})));return function(t){return e.apply(this,arguments)}}()},{key:"AllowApp",value:function(){var e=(0,a.Z)((0,r.Z)().mark((function e(t){var n,a;return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return n=this.createAxiosClient(),e.next=3,n.post("appmanagement/allow",t);case 3:a=e.sent,console.log(a);case 5:case"end":return e.stop()}}),e,this)})));return function(t){return e.apply(this,arguments)}}()}],[{key:"getInstance",value:function(e){return n.instance||(n.instance=new n(e)),n.instance}}]),n}(n(2008).$);p.instance=void 0},9779:function(e,t,n){var r=this&&this.__extends||function(){var e=function(t,n){return e=Object.setPrototypeOf||{__proto__:[]}instanceof Array&&function(e,t){e.__proto__=t}||function(e,t){for(var n in t)t.hasOwnProperty(n)&&(e[n]=t[n])},e(t,n)};return function(t,n){function r(){this.constructor=t}e(t,n),t.prototype=null===n?Object.create(n):(r.prototype=n.prototype,new r)}}(),a=this&&this.__assign||function(){return a=Object.assign||function(e){for(var t,n=1,r=arguments.length;n<r;n++)for(var a in t=arguments[n])Object.prototype.hasOwnProperty.call(t,a)&&(e[a]=t[a]);return e},a.apply(this,arguments)},s=this&&this.__rest||function(e,t){var n={};for(var r in e)Object.prototype.hasOwnProperty.call(e,r)&&t.indexOf(r)<0&&(n[r]=e[r]);if(null!=e&&"function"===typeof Object.getOwnPropertySymbols){var a=0;for(r=Object.getOwnPropertySymbols(e);a<r.length;a++)t.indexOf(r[a])<0&&(n[r[a]]=e[r[a]])}return n},o=this&&this.__importStar||function(e){if(e&&e.__esModule)return e;var t={};if(null!=e)for(var n in e)Object.hasOwnProperty.call(e,n)&&(t[n]=e[n]);return t.default=e,t},i=o(n(2007)),c=o(n(2791));function p(e,t,n){var r=function(e,t,n){var r=n[e+"Style"]||"",a=t&&t[e]||"";return r?r+";"+a:a}(e,t,n);return r?' style="'+r+'"':""}var u={'"':"&quot;","'":"&apos;","&":"&amp;",">":"&gt;","<":"&lt"};var l=function(e){function t(){return null!==e&&e.apply(this,arguments)||this}return r(t,e),t.prototype.render=function(){var e,t=this.props,n=t.json,r=t.data,o=t.replacer,i=t.space,l=t.themeClassName,f=t.theme,d=t.onJSONPrettyError,h=t.onError,y=t.silent,v=t.mainStyle,m=t.keyStyle,g=t.valueStyle,x=t.stringStyle,_=t.booleanStyle,w=t.errorStyle,b=s(t,["json","data","replacer","space","themeClassName","theme","onJSONPrettyError","onError","silent","mainStyle","keyStyle","valueStyle","stringStyle","booleanStyle","errorStyle"]),j={mainStyle:v,keyStyle:m,valueStyle:g,stringStyle:x,booleanStyle:_,errorStyle:w},Z=r||n;if("string"===typeof Z)try{Z=JSON.parse(Z)}catch(S){return y||console.warn("[react-json-pretty]: "+S.message),d&&d(S),!d&&h&&(h(S),console.warn("JSONPretty#onError is deprecated, please use JSONPretty#onJSONPrettyError instead")),c.createElement("div",a({},b,{dangerouslySetInnerHTML:{__html:'<pre class="__json-pretty-error__"'+p("error",f,j)+">"+(e=Z,(e?e.replace(/<|>|&|"|'/g,(function(e){return u[e]})):e)+"</pre>")}}))}return c.createElement("div",a({},b,{dangerouslySetInnerHTML:{__html:'<pre class="'+l+'"'+p("main",f,j)+">"+this._pretty(f,Z,o,+i,j)+"</pre>"}}))},t.prototype._pretty=function(e,t,n,r,a){var s=JSON.stringify(t,"function"===typeof n?n:null,isNaN(r)?2:r);return s?s.replace(/&/g,"&amp;").replace(/\\"([^,])/g,"\\&quot;$1").replace(/</g,"&lt;").replace(/>/g,"&gt;").replace(/^( *)("[^"]+": )?("[^"]*"|[\w.+-]*)?([,[{]|\[\s*\],?|\{\s*\},?)?$/gm,this._replace.bind(null,e,a)):s},t.prototype._replace=function(e,t,n,r,a,s,o){var i="</span>",c='<span class="__json-key__"'+p("key",e,t)+">",u='<span class="__json-value__"'+p("value",e,t)+">",l='<span class="__json-string__"'+p("string",e,t)+">",f='<span class="__json-boolean__"'+p("boolean",e,t)+">",d=r||"";return a&&(d=d+'"'+c+a.replace(/^"|":\s$/g,"")+'</span>": '),s&&(d="true"===s||"false"===s?d+f+s+i:d+('"'===s[0]?l:u)+s+i),d+(o||"")},t.propTypes={data:i.any,json:i.any,replacer:i.func,silent:i.bool,space:i.oneOfType([i.number,i.string]),theme:i.object,themeClassName:i.string,onJSONPrettyError:i.func},t.defaultProps={data:"",json:"",silent:!0,space:2,themeClassName:"__json-pretty__"},t}(c.Component);e.exports=l},3689:function(e){e.exports={main:"line-height:1.3;color:#66d9ef;background:#272822;overflow:auto;",error:"line-height:1.3;color:#66d9ef;background:#272822;overflow:auto;",key:"color:#f92672;",string:"color:#fd971f;",value:"color:#a6e22e;",boolean:"color:#ac81fe;"}}}]);
//# sourceMappingURL=654.a5005535.chunk.js.map