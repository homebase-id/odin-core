"use strict";(self.webpackChunkowner_app=self.webpackChunkowner_app||[]).push([[603],{2284:function(e,t,r){var n=r(885),a=r(2791),i=r(1261),s=r(4648),o=r(245),c=r(184),u=function(e){var t=e.className,r=e.isChecked,n=e.children,a=e.onChange;return(0,c.jsxs)("li",{className:"flex min-w-[6rem] cursor-pointer select-none flex-row py-1 px-4 hover:bg-slate-100 dark:hover:bg-slate-700 ".concat(t),onClick:a,children:[(0,c.jsx)(s.Z,{className:"my-auto mr-3 h-4 w-4 ".concat(r?"text-slate-700 dark:text-slate-200":"text-transparent")})," ",(0,c.jsx)("span",{className:"mr-auto block h-full py-1",children:n})]})};t.Z=function(e){var t=e.className,r=e.permissionLevels,s=e.onChange,l=e.defaultValue,p=(0,a.useState)(!1),d=(0,n.Z)(p,2),f=d[0],v=d[1],h=(0,a.useState)(l),m=(0,n.Z)(h,2),Z=m[0],x=m[1],y=(0,a.useRef)(null);(0,i.Z)(y,(function(){return v(!1)}));var w=r.reduce((function(e,t){return t.value>e.value&&t.value<=l?t:e}),r[0]),g=function(e){x(e),s(e)};return(0,c.jsx)("div",{className:null!==t&&void 0!==t?t:"",children:(0,c.jsxs)("div",{className:"relative cursor-pointer rounded-md bg-slate-100 dark:bg-slate-800",onClick:function(){return v(!f)},ref:y,children:[(0,c.jsxs)("div",{className:"flex min-w-[6rem] flex-row py-1 px-2",children:[(0,c.jsx)("span",{className:"my-auto mr-2 select-none",children:w.name})," ",(0,c.jsx)(o.Z,{className:"my-auto ml-auto h-2 w-2 rotate-90"})]}),(0,c.jsx)("ul",{className:"absolute top-[100%] right-0 overflow-hidden bg-white dark:bg-slate-800 ".concat(f?"z-10 max-h-[30rem] border border-slate-100 py-3 shadow-2xl dark:border-slate-700":"max-h-0"),children:r.map((function(e){return(0,c.jsx)(u,{isChecked:0===e.value?Z===e.value:Z>=e.value,onChange:function(){return Z!==e.value?g(e.value):g(0)},children:e.name},e.value)}))})]})})}},245:function(e,t,r){var n=r(184);t.Z=function(e){var t=e.className;return(0,n.jsx)("svg",{xmlns:"http://www.w3.org/2000/svg",viewBox:"0 0 384 512",fill:"currentColor",className:t,children:(0,n.jsx)("path",{d:"M73 39c-14.8-9.1-33.4-9.4-48.5-.9S0 62.6 0 80V432c0 17.4 9.4 33.4 24.5 41.9s33.7 8.1 48.5-.9L361 297c14.3-8.7 23-24.2 23-41s-8.7-32.2-23-41L73 39z"})})}},8491:function(e,t,r){var n=r(184);t.Z=function(e){var t=e.title,r=e.className,a=e.children,i=e.isOpaqueBg,s=void 0!==i&&i,o=e.isBorderLess,c=void 0!==o&&o;return(0,n.jsxs)("section",{className:"my-5 rounded-md ".concat(s?c?"":"rounded-lg border-[1px] border-gray-200 border-opacity-80 px-5 dark:border-gray-700":"bg-slate-50 px-5 dark:bg-slate-800"," dark:text-slate-300 ").concat(null!==r&&void 0!==r?r:""),children:[t?(0,n.jsx)("div",{className:"relative border-b-[1px] border-gray-200 border-opacity-80 py-5 transition-all duration-300 dark:border-gray-700",children:(0,n.jsx)("h3",{className:"text-2xl dark:text-white",children:t})}):null,(0,n.jsx)("div",{className:"py-5 ",children:a})]})}},5163:function(e,t,r){r.r(t),r.d(t,{default:function(){return D}});var n=r(885),a=r(2791),i=r(6871),s=r(4990),o=r(1512),c=r(9072),u=r(1413),l=r(2982),p=r(4165),d=r(5861),f=r(4164),v=r(4395),h=r(3412),m=r(2562),Z=r(2284),x=r(715),y=r(6123),w=r(184),g=function(e){var t=e.title,r=e.confirmText,i=e.isOpen,o=e.driveDefinition,g=e.onConfirm,k=e.onCancel,b=(0,h.Z)("modal-container"),j=o.targetDriveInfo,C=(0,a.useState)("idle"),A=(0,n.Z)(C,2),N=A[0],O=A[1],R=(0,v.Z)(),I=R.fetch.data,D=R.createOrUpdate.mutateAsync,S=(0,a.useState)([]),G=(0,n.Z)(S,2),E=G[0],F=G[1];if(!i)return null;var P=(0,w.jsx)(y.Z,{title:t,onClose:k,children:(0,w.jsx)(w.Fragment,{children:(0,w.jsxs)("form",{onSubmit:function(){var e=(0,d.Z)((0,p.Z)().mark((function e(t){return(0,p.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return t.preventDefault(),O("loading"),e.prev=2,e.next=5,Promise.all(E.map(function(){var e=(0,d.Z)((0,p.Z)().mark((function e(t){return(0,p.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return e.next=2,D({circleDefinition:t});case 2:return e.abrupt("return",e.sent);case 3:case"end":return e.stop()}}),e)})));return function(t){return e.apply(this,arguments)}}()));case 5:e.next=10;break;case 7:e.prev=7,e.t0=e.catch(2),O("error");case 10:return O("success"),g(),e.abrupt("return",!1);case 13:case"end":return e.stop()}}),e,null,[[2,7]])})));return function(t){return e.apply(this,arguments)}}(),children:[null!==I&&void 0!==I&&I.length?(0,w.jsxs)(w.Fragment,{children:[(0,w.jsxs)("h2",{className:"mb-2 flex flex-row text-xl",children:[(0,w.jsx)(x.Z,{className:"my-auto mr-2 h-4 w-4"})," ",(0,s.t)("Circles"),":"]}),I.map((function(e){var t,r=null===E||void 0===E?void 0:E.find((function(t){return t.id===e.id})),n=null===(t=(null!==r&&void 0!==r?r:e).drivesGrants)||void 0===t?void 0:t.find((function(e){return e.drive.alias===j.alias&&e.drive.type===j.type}));return(0,w.jsxs)("div",{className:"mb-5 flex flex-row",children:[(0,w.jsx)("label",{htmlFor:"name",className:"my-auto mr-2",children:e.name}),(0,w.jsx)(Z.Z,{className:"ml-auto",permissionLevels:m.A,defaultValue:null===n||void 0===n?void 0:n.permission,onChange:function(t){F([].concat((0,l.Z)(E.filter((function(t){return t.id!==e.id}))),[(0,u.Z)((0,u.Z)({},e),{},{drivesGrants:[].concat((0,l.Z)(e.drivesGrants.filter((function(e){return e.drive.alias!==j.alias&&e.drive.type!==j.type}))),[{drive:(0,u.Z)({},j),permission:t}])})]))}})]},e.id)}))]}):null,(0,w.jsxs)("div",{className:"-mx-2 py-3 sm:flex sm:flex-row-reverse",children:[(0,w.jsx)(c.Z,{className:"mx-2",icon:"send",state:N,children:r||(0,s.t)("Save")}),(0,w.jsx)(c.Z,{className:"mx-2",type:"secondary",onClick:k,children:(0,s.t)("Cancel")})]})]})})});return(0,f.createPortal)(P,b)},k=r(144),b=r(3504),j=function(e){var t=e.appDef,r=e.permissionLevel;return t?(0,w.jsx)("div",{className:"mb-4",children:(0,w.jsx)(b.rU,{to:"/owner/apps/".concat(t.appId),children:(0,w.jsxs)("h2",{className:"mb-2 flex flex-row text-xl",children:[(0,w.jsx)(x.Z,{className:"my-auto mr-2 h-4 w-4"})," ",(0,w.jsx)("span",{className:"my-auto",children:t.name}),r&&(0,w.jsxs)("span",{className:"my-auto",children:[": ",r]})]})})}):(0,w.jsx)(w.Fragment,{})},C=r(8808),A=r(8491),N=r(7266),O=function(e){var t=e.driveDefinition,r=(0,v.Z)().fetch.data,n=(0,k.Z)().fetchRegistered.data,a=t.targetDriveInfo,i=null===r||void 0===r?void 0:r.filter((function(e){var t;return null===(t=e.drivesGrants)||void 0===t?void 0:t.some((function(e){return e.drive.alias===a.alias&&e.drive.type===a.type}))})),o=null===n||void 0===n?void 0:n.filter((function(e){return e.grant.driveGrants.some((function(e){return e.drive.alias===a.alias&&e.drive.type===a.type}))}));return(0,w.jsxs)(w.Fragment,{children:[null!==i&&void 0!==i&&i.length?(0,w.jsx)(A.Z,{title:(0,s.t)("Circles with access:"),isOpaqueBg:!0,children:(0,w.jsx)("ul",{children:i.map((function(e){var t=e.drivesGrants.find((function(e){return e.drive.alias===a.alias&&e.drive.type===a.type}));return(0,w.jsx)(C.Z,{circleDef:e,permissionLevel:(0,N.hz)(null===t||void 0===t?void 0:t.permission,m.A).name},e.id)}))})}):null,null!==o&&void 0!==o&&o.length?(0,w.jsx)(A.Z,{title:(0,s.t)("Apps with access:"),isOpaqueBg:!0,children:(0,w.jsx)("ul",{children:o.map((function(e){var t=e.grant.driveGrants.find((function(e){return e.drive.alias===a.alias&&e.drive.type===a.type}));return(0,w.jsx)(j,{appDef:e,permissionLevel:(0,N.hz)(null===t||void 0===t?void 0:t.permission,m.A).name},e.appId)}))})}):null]})},R=r(894),I=r(3004),D=function(){var e,t=(0,i.UO)().driveKey.split("_"),r=(0,o.Z)({targetDrive:{alias:t[0],type:t[1]}}).fetch,u=r.data,l=r.isLoading,p=(0,a.useState)(!1),d=(0,n.Z)(p,2),f=d[0],v=d[1];return l&&w.Fragment,u?(0,w.jsxs)(w.Fragment,{children:[(0,w.jsx)(I.Z,{icon:R.Z,title:"".concat(u.name),actions:(0,w.jsx)(w.Fragment,{children:(0,w.jsx)(c.Z,{onClick:function(){return v(!0)},children:"Edit Access"})}),breadCrumbs:[{href:"/owner/drives",title:"My Drives"},{title:null!==(e=u.name)&&void 0!==e?e:""}]}),(0,w.jsxs)(A.Z,{title:(0,s.t)("Metadata"),isOpaqueBg:!0,children:[(0,w.jsx)("p",{children:u.metadata}),(0,w.jsxs)("ul",{children:[u.allowAnonymousReads?(0,w.jsx)("li",{children:"Allow Anonymous Reads"}):null,u.isReadonly?(0,w.jsx)("li",{children:"Read Only"}):null]})]}),(0,w.jsx)(O,{driveDefinition:u}),(0,w.jsx)(g,{driveDefinition:u,isOpen:f,onCancel:function(){v(!1)},onConfirm:function(){v(!1)},title:"".concat((0,s.t)("Edit access on")," ").concat(u.name)})]}):(0,w.jsx)(w.Fragment,{children:"No matching drive found"})}},144:function(e,t,r){var n=r(4165),a=r(5861),i=r(7408),s=r(4180),o=r(6117);t.Z=function(){var e=(0,o.Z)().getSharedSecret,t=s.Z.getInstance(e()),r=function(){var e=(0,a.Z)((0,n.Z)().mark((function e(){var r;return(0,n.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return e.next=2,t.GetAppRegistrations();case 2:return r=e.sent,e.abrupt("return",null===r||void 0===r?void 0:r.sort((function(e,t){return(e.isRevoked?1:0)-(t.isRevoked?1:0)})));case 4:case"end":return e.stop()}}),e)})));return function(){return e.apply(this,arguments)}}();return{fetchRegistered:(0,i.useQuery)(["registeredApps"],(function(){return r()}),{refetchOnWindowFocus:!1})}}},4395:function(e,t,r){var n=r(4165),a=r(5861),i=r(7408),s=r(136),o=r(3766),c=r(6117);t.Z=function(){var e=(0,i.useQueryClient)(),t=(0,c.Z)().getSharedSecret,r=o.k.getInstance(t()),u=s.W.getInstance(t()),l=function(){var e=(0,a.Z)((0,n.Z)().mark((function e(){return(0,n.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return e.next=2,r.getCircles();case 2:return e.abrupt("return",e.sent);case 3:case"end":return e.stop()}}),e)})));return function(){return e.apply(this,arguments)}}(),p=function(){var e=(0,a.Z)((0,n.Z)().mark((function e(t){var a;return(0,n.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:if(!(a=t.circleDefinition).id){e.next=7;break}return e.next=4,r.updateDefinition(a);case 4:case 9:return e.abrupt("return",e.sent);case 7:return e.next=9,r.createDefinition(a);case 10:case"end":return e.stop()}}),e)})));return function(t){return e.apply(this,arguments)}}(),d=function(){var e=(0,a.Z)((0,n.Z)().mark((function e(t){var r,a;return(0,n.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return r=t.circleId,a=t.dotYouId,e.next=3,u.add({circleId:r,dotYouId:a});case 3:return e.abrupt("return",e.sent);case 4:case"end":return e.stop()}}),e)})));return function(t){return e.apply(this,arguments)}}(),f=function(){var e=(0,a.Z)((0,n.Z)().mark((function e(t){var r,a;return(0,n.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return r=t.circleId,a=t.dotYouId,e.next=3,u.remove({circleId:r,dotYouId:a});case 3:return e.abrupt("return",e.sent);case 4:case"end":return e.stop()}}),e)})));return function(t){return e.apply(this,arguments)}}();return{fetch:(0,i.useQuery)(["circles"],(function(){return l()}),{refetchOnWindowFocus:!1}),createOrUpdate:(0,i.useMutation)(p,{onSuccess:function(t,r){e.invalidateQueries(["circles"])},onError:function(e){console.error(e)}}),provideGrant:(0,i.useMutation)(d,{onSuccess:function(){var t=(0,a.Z)((0,n.Z)().mark((function t(r,a){return(0,n.Z)().wrap((function(t){for(;;)switch(t.prev=t.next){case 0:e.invalidateQueries(["circles"]),e.invalidateQueries(["connectionInfo",a.dotYouId]);case 2:case"end":return t.stop()}}),t)})));return function(e,r){return t.apply(this,arguments)}}(),onError:function(e){console.error(e)}}),revokeGrant:(0,i.useMutation)(f,{onSuccess:function(){var t=(0,a.Z)((0,n.Z)().mark((function t(r,a){return(0,n.Z)().wrap((function(t){for(;;)switch(t.prev=t.next){case 0:e.invalidateQueries(["circles"]),e.invalidateQueries(["connectionInfo",a.dotYouId]);case 2:case"end":return t.stop()}}),t)})));return function(e,r){return t.apply(this,arguments)}}(),onError:function(e){console.error(e)}})}}},4180:function(e,t,r){r.d(t,{Z:function(){return u}});var n=r(4165),a=r(5861),i=r(5671),s=r(3144),o=r(9340),c=r(1129),u=function(e){(0,o.Z)(r,e);var t=(0,c.Z)(r);function r(e){return(0,i.Z)(this,r),t.call(this,e)}return(0,s.Z)(r,[{key:"RegisterAppClient",value:function(){var e=(0,a.Z)((0,n.Z)().mark((function e(t){var r,a;return(0,n.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return r=this.createAxiosClient(),e.next=3,r.post("appmanagement/register/client",t);case 3:return a=e.sent,e.abrupt("return",a.data);case 5:case"end":return e.stop()}}),e,this)})));return function(t){return e.apply(this,arguments)}}()},{key:"RegisterChatAppClient_temp",value:function(){var e=(0,a.Z)((0,n.Z)().mark((function e(t){var r,a;return(0,n.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return r=this.createAxiosClient(),e.next=3,r.post("appmanagement/register/chatclient_temp",t);case 3:return a=e.sent,console.log("RegisterChatAppClient_temp returning response"),console.log(a),e.abrupt("return",a.data);case 7:case"end":return e.stop()}}),e,this)})));return function(t){return e.apply(this,arguments)}}()},{key:"RegisterApp",value:function(){var e=(0,a.Z)((0,n.Z)().mark((function e(t){var r,a;return(0,n.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return r=this.createAxiosClient(),e.next=3,r.post("appmanagement/register/app",t);case 3:return a=e.sent,console.log("RegisterApp returning response"),console.log(a),e.abrupt("return",a.data);case 7:case"end":return e.stop()}}),e,this)})));return function(t){return e.apply(this,arguments)}}()},{key:"GetAppRegistration",value:function(){var e=(0,a.Z)((0,n.Z)().mark((function e(t){var r,a;return(0,n.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return r=this.createAxiosClient(),e.next=3,r.post("appmanagement/app",t);case 3:return a=e.sent,e.abrupt("return",a.data);case 5:case"end":return e.stop()}}),e,this)})));return function(t){return e.apply(this,arguments)}}()},{key:"GetAppRegistrations",value:function(){var e=(0,a.Z)((0,n.Z)().mark((function e(){var t,r;return(0,n.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return t=this.createAxiosClient(),e.next=3,t.get("appmanagement/list");case 3:return r=e.sent,e.abrupt("return",r.data);case 5:case"end":return e.stop()}}),e,this)})));return function(){return e.apply(this,arguments)}}()},{key:"RevokeApp",value:function(){var e=(0,a.Z)((0,n.Z)().mark((function e(t){var r;return(0,n.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return r=this.createAxiosClient(),e.next=3,r.post("appmanagement/revoke",t);case 3:case"end":return e.stop()}}),e,this)})));return function(t){return e.apply(this,arguments)}}()},{key:"AllowApp",value:function(){var e=(0,a.Z)((0,n.Z)().mark((function e(t){var r,a;return(0,n.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return r=this.createAxiosClient(),e.next=3,r.post("appmanagement/allow",t);case 3:a=e.sent,console.log(a);case 5:case"end":return e.stop()}}),e,this)})));return function(t){return e.apply(this,arguments)}}()}],[{key:"getInstance",value:function(e){return r.instance||(r.instance=new r(e)),r.instance}}]),r}(r(2008).$);u.instance=void 0},136:function(e,t,r){r.d(t,{W:function(){return p}});var n=r(4165),a=r(5861),i=r(5671),s=r(3144),o=r(1752),c=r(1120),u=r(9340),l=r(1129),p=function(e){(0,u.Z)(r,e);var t=(0,l.Z)(r);function r(e){var n;if((0,i.Z)(this,r),!e)throw"Shared Secret is required";return(n=t.call(this,e)).root="/circles/connections/circles",n}return(0,s.Z)(r,[{key:"add",value:function(){var e=(0,a.Z)((0,n.Z)().mark((function e(t){var a,i;return(0,n.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return a=(0,o.Z)((0,c.Z)(r.prototype),"createAxiosClient",this).call(this),i=this.root+"/add",e.abrupt("return",a.post(i,t).then((function(e){return e.data})).catch((0,o.Z)((0,c.Z)(r.prototype),"handleErrorResponse",this)));case 3:case"end":return e.stop()}}),e,this)})));return function(t){return e.apply(this,arguments)}}()},{key:"remove",value:function(){var e=(0,a.Z)((0,n.Z)().mark((function e(t){var a,i;return(0,n.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return a=(0,o.Z)((0,c.Z)(r.prototype),"createAxiosClient",this).call(this),i=this.root+"/revoke",e.abrupt("return",a.post(i,t).then((function(e){return e.data})).catch((0,o.Z)((0,c.Z)(r.prototype),"handleErrorResponse",this)));case 3:case"end":return e.stop()}}),e,this)})));return function(t){return e.apply(this,arguments)}}()},{key:"fetchMembers",value:function(){var e=(0,a.Z)((0,n.Z)().mark((function e(t){var a,i;return(0,n.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return a=(0,o.Z)((0,c.Z)(r.prototype),"createAxiosClient",this).call(this),i=this.root+"/list",e.abrupt("return",a.post(i,{circleId:t}).then((function(e){return e.data})).catch((0,o.Z)((0,c.Z)(r.prototype),"handleErrorResponse",this)));case 3:case"end":return e.stop()}}),e,this)})));return function(t){return e.apply(this,arguments)}}()}],[{key:"getInstance",value:function(e){return r.instance||(r.instance=new r(e)),r.instance}}]),r}(r(2008).$);p.instance=void 0},3766:function(e,t,r){r.d(t,{k:function(){return d}});var n=r(4165),a=r(1413),i=r(5861),s=r(5671),o=r(3144),c=r(1752),u=r(1120),l=r(9340),p=r(1129),d=function(e){(0,l.Z)(r,e);var t=(0,p.Z)(r);function r(e){var n;if((0,s.Z)(this,r),!e)throw"Shared Secret is required";return(n=t.call(this,e)).root="/circles/definitions",n}return(0,o.Z)(r,[{key:"updateDefinition",value:function(){var e=(0,i.Z)((0,n.Z)().mark((function e(t){var i,s,o;return(0,n.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return i=(0,c.Z)((0,u.Z)(r.prototype),"createAxiosClient",this).call(this),s=this.root+"/update",(o=(0,a.Z)({},t)).drivesGrants=o.drivesGrants||o.drives,o.drives=void 0,console.log("actual update data:",o),e.abrupt("return",i.post(s,o).then((function(e){return e.data})).catch((0,c.Z)((0,u.Z)(r.prototype),"handleErrorResponse",this)));case 7:case"end":return e.stop()}}),e,this)})));return function(t){return e.apply(this,arguments)}}()},{key:"createDefinition",value:function(){var e=(0,i.Z)((0,n.Z)().mark((function e(t){var a,i;return(0,n.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return a=(0,c.Z)((0,u.Z)(r.prototype),"createAxiosClient",this).call(this),i=this.root+"/create",e.abrupt("return",a.post(i,t).then((function(e){return e.data})).catch((0,c.Z)((0,u.Z)(r.prototype),"handleErrorResponse",this)));case 3:case"end":return e.stop()}}),e,this)})));return function(t){return e.apply(this,arguments)}}()},{key:"getCircles",value:function(){var e=(0,i.Z)((0,n.Z)().mark((function e(){var t,i;return(0,n.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return t=(0,c.Z)((0,u.Z)(r.prototype),"createAxiosClient",this).call(this),i=this.root+"/list",e.abrupt("return",t.get(i).then((function(e){return e.data.map((function(e){var t;return e.drivesGrants=null===(t=e.drivesGrants)||void 0===t?void 0:t.map((function(e){return(0,a.Z)((0,a.Z)({},e),{},{permission:"Read"===e.permission?2:"Write"===e.permission?4:"ReadWrite"===e.permission?5:e.permission})})),e}))})).catch((0,c.Z)((0,u.Z)(r.prototype),"handleErrorResponse",this)));case 3:case"end":return e.stop()}}),e,this)})));return function(){return e.apply(this,arguments)}}()},{key:"getCircle",value:function(){var e=(0,i.Z)((0,n.Z)().mark((function e(t){var i,s;return(0,n.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return i=(0,c.Z)((0,u.Z)(r.prototype),"createAxiosClient",this).call(this),s=this.root+"/circle",e.abrupt("return",i.post(s,t).then((function(e){return e.data})).then((function(e){var t;return e.drivesGrants=null===(t=e.drivesGrants)||void 0===t?void 0:t.map((function(e){return(0,a.Z)((0,a.Z)({},e),{},{permission:"Read"===e.permission?2:"Write"===e.permission?4:"ReadWrite"===e.permission?5:e.permission})})),e})).catch((0,c.Z)((0,u.Z)(r.prototype),"handleErrorResponse",this)));case 3:case"end":return e.stop()}}),e,this)})));return function(t){return e.apply(this,arguments)}}()},{key:"removeCircle",value:function(){var e=(0,i.Z)((0,n.Z)().mark((function e(t){var a,i;return(0,n.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return a=(0,c.Z)((0,u.Z)(r.prototype),"createAxiosClient",this).call(this),i=this.root+"/delete",e.abrupt("return",a.post(i,t).then((function(e){return e.data})).catch((0,c.Z)((0,u.Z)(r.prototype),"handleErrorResponse",this)));case 3:case"end":return e.stop()}}),e,this)})));return function(t){return e.apply(this,arguments)}}()}],[{key:"getInstance",value:function(e){return r.instance||(r.instance=new r(e)),r.instance}}]),r}(r(2008).$);d.instance=void 0},4942:function(e,t,r){function n(e,t,r){return t in e?Object.defineProperty(e,t,{value:r,enumerable:!0,configurable:!0,writable:!0}):e[t]=r,e}r.d(t,{Z:function(){return n}})},1413:function(e,t,r){r.d(t,{Z:function(){return i}});var n=r(4942);function a(e,t){var r=Object.keys(e);if(Object.getOwnPropertySymbols){var n=Object.getOwnPropertySymbols(e);t&&(n=n.filter((function(t){return Object.getOwnPropertyDescriptor(e,t).enumerable}))),r.push.apply(r,n)}return r}function i(e){for(var t=1;t<arguments.length;t++){var r=null!=arguments[t]?arguments[t]:{};t%2?a(Object(r),!0).forEach((function(t){(0,n.Z)(e,t,r[t])})):Object.getOwnPropertyDescriptors?Object.defineProperties(e,Object.getOwnPropertyDescriptors(r)):a(Object(r)).forEach((function(t){Object.defineProperty(e,t,Object.getOwnPropertyDescriptor(r,t))}))}return e}}}]);
//# sourceMappingURL=603.d392dcd9.chunk.js.map