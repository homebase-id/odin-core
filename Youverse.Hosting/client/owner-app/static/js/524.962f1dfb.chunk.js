"use strict";(self.webpackChunkowner_app=self.webpackChunkowner_app||[]).push([[524],{1524:function(e,n,t){t.r(n);var r=t(4165),a=t(5861),s=t(885),i=t(6117),o=t(2420),c=t(2791),u=t(3504),p=t(7165),l=t(6752),d=t(184);n.default=function(){var e=(0,i.Z)().getSharedSecret,n=(0,u.lr)(),t=(0,s.Z)(n,1)[0],f=(0,c.useState)(),h=(0,s.Z)(f,2),v=h[0],x=h[1],g=(0,c.useState)(),m=(0,s.Z)(g,2),C=m[0],Z=(m[1],(0,c.useState)(null)),w=(0,s.Z)(Z,2),b=w[0],R=w[1],k=(0,c.useState)(null),y=(0,s.Z)(k,2),A=y[0],j=y[1],N=(0,c.useState)(null),S=(0,s.Z)(N,2),I=S[0],q=S[1],D=(0,c.useState)(),_=(0,s.Z)(D,2),O=_[0],U=_[1],F=new p.Z(e()),W=new l.l$({api:l.Ii.Owner,sharedSecret:e()}),E={drive:{alias:"faaaaaaa-2d68-4dd2-8196-669c21e927ea",type:"fabbbbbb-2d68-4dd2-8196-669c21e927ea"},permission:o.n.Read|o.n.Write},G={permissions:o.r.ReadConnectionRequests|o.r.ReadConnections};(0,c.useEffect)((function(){var e=function(){var e=(0,a.Z)((0,r.Z)().mark((function e(){var n,a,s;return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return a={appId:(n={appId:"217d3338-afee-4529-aa7f-d8442bc11f25",name:"Chatrbox",permissionSet:G,drives:[E]}).appId,clientPublicKey64:t.get("pk"),clientFriendlyName:t.get("fn")},e.next=4,F.GetAppRegistration({appId:n.appId});case 4:s=e.sent,q(s),R(n),j(a),U(t.get("rs")),x(!0);case 10:case"end":return e.stop()}}),e)})));return function(){return e.apply(this,arguments)}}();e()}),[t]);var K=function(){var e=(0,a.Z)((0,r.Z)().mark((function e(){return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return e.next=2,W.EnsureDrive(E.drive,"Chat Drive","Chatr drive",!1);case 2:return e.next=4,F.RegisterApp(b);case 4:return e.next=6,$();case 6:case"end":return e.stop()}}),e)})));return function(){return e.apply(this,arguments)}}(),$=function(){var e=(0,a.Z)((0,r.Z)().mark((function e(){var n,t,a,s;return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return e.next=2,F.RegisterChatAppClient_temp(A);case 2:n=e.sent,t=encodeURI(n.data),a=l.j4.base64ToUint8Array(t),console.log("bytes",a),s="".concat(O,"d=").concat(t,"&v=").concat(n.encryptionVersion),window.location.href=s;case 8:case"end":return e.stop()}}),e)})));return function(){return e.apply(this,arguments)}}();return v?C?(0,d.jsxs)("div",{className:"container mx-auto",children:["browser will be redirected to ",(0,d.jsx)("br",{}),(0,d.jsx)("span",{children:C})]}):(0,d.jsx)("section",{className:"mt-20",children:(0,d.jsx)("div",{className:"container mx-auto",children:(0,d.jsxs)("div",{className:"-m-5 flex flex-wrap",children:[v&&null==I&&(0,d.jsxs)(d.Fragment,{children:[(0,d.jsxs)("div",{className:"px-5",children:["App with id ",b.appId," is not registered.",(0,d.jsx)("br",{}),"Clicking OK will approve the app for use and register this client"]}),(0,d.jsx)("div",{className:"px-5",children:(0,d.jsx)("a",{onClick:K,className:"mt-10 block rounded border-0 bg-green-500 py-2 px-4 text-white hover:bg-green-600 focus:outline-none",children:"Register Now"})})]}),v&&null!=I&&(0,d.jsxs)(d.Fragment,{children:[(0,d.jsxs)("div",{className:"px-5",children:["App with id ",null===I||void 0===I?void 0:I.appId," is registered at ",null===I||void 0===I?void 0:I.created]}),(0,d.jsx)("div",{className:"px-5",children:(0,d.jsx)("a",{onClick:$,className:"mt-10 block rounded border-0 bg-green-500 py-2 px-4 text-white hover:bg-green-600 focus:outline-none",children:"Register this Client"})})]})]})})}):(0,d.jsx)("div",{children:"Loading..."})}},7165:function(e,n,t){t.d(n,{Z:function(){return u}});var r=t(4165),a=t(5861),s=t(5671),i=t(3144),o=t(9340),c=t(1129),u=function(e){(0,o.Z)(t,e);var n=(0,c.Z)(t);function t(e){return(0,s.Z)(this,t),n.call(this,e)}return(0,i.Z)(t,[{key:"RegisterAppClient",value:function(){var e=(0,a.Z)((0,r.Z)().mark((function e(n){var t,a;return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return t=this.createAxiosClient(),e.next=3,t.post("appmanagement/register/client",n);case 3:return a=e.sent,e.abrupt("return",a.data);case 5:case"end":return e.stop()}}),e,this)})));return function(n){return e.apply(this,arguments)}}()},{key:"RegisterChatAppClient_temp",value:function(){var e=(0,a.Z)((0,r.Z)().mark((function e(n){var t,a;return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return t=this.createAxiosClient(),e.next=3,t.post("appmanagement/register/chatclient_temp",n);case 3:return a=e.sent,console.log("RegisterChatAppClient_temp returning response"),console.log(a),e.abrupt("return",a.data);case 7:case"end":return e.stop()}}),e,this)})));return function(n){return e.apply(this,arguments)}}()},{key:"RegisterApp",value:function(){var e=(0,a.Z)((0,r.Z)().mark((function e(n){var t,a;return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return t=this.createAxiosClient(),e.next=3,t.post("appmanagement/register/app",n);case 3:return a=e.sent,console.log("RegisterApp returning response"),console.log(a),e.abrupt("return",a.data);case 7:case"end":return e.stop()}}),e,this)})));return function(n){return e.apply(this,arguments)}}()},{key:"GetAppRegistration",value:function(){var e=(0,a.Z)((0,r.Z)().mark((function e(n){var t,a;return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return t=this.createAxiosClient(),e.next=3,t.post("appmanagement/app",n);case 3:return a=e.sent,e.abrupt("return",a.data);case 5:case"end":return e.stop()}}),e,this)})));return function(n){return e.apply(this,arguments)}}()}]),t}(t(2008).$)},2420:function(e,n,t){var r,a;t.d(n,{n:function(){return r},r:function(){return a}}),function(e){e[e.None=0]="None",e[e.Read=1]="Read",e[e.Write=4]="Write"}(r||(r={})),function(e){e[e.None=0]="None",e[e.ApproveConnection=1]="ApproveConnection",e[e.ReadConnections=2]="ReadConnections",e[e.UpdateConnections=4]="UpdateConnections",e[e.DeleteConnections=8]="DeleteConnections",e[e.CreateOrSendConnectionRequests=16]="CreateOrSendConnectionRequests",e[e.ReadConnectionRequests=32]="ReadConnectionRequests",e[e.DeleteConnectionRequests=64]="DeleteConnectionRequests"}(a||(a={}))}}]);
//# sourceMappingURL=524.962f1dfb.chunk.js.map