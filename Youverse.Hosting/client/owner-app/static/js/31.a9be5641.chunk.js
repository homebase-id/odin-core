"use strict";(self.webpackChunkowner_app=self.webpackChunkowner_app||[]).push([[31],{9636:function(e,n,t){var r=t(1413),i=t(885),o=t(2791),a=t(4164),s=t(4990),c=t(3412),l=t(3431),u=t(9072),d=t(6916),f=t(2465),p=t(6123),m=t(184);n.Z=function(e){var n=e.title,t=e.confirmText,h=e.isOpen,x=e.defaultValue,v=e.onConfirm,Z=e.onCancel,j=(0,c.Z)("modal-container"),g=(0,l.Z)().saveProfile,y=g.mutate,b=g.status,w=(0,o.useState)((0,r.Z)({},x)),I=(0,i.Z)(w,2),S=I[0],N=I[1];if(!h)return null;var C=(0,m.jsx)(p.Z,{title:n,onClose:Z,children:(0,m.jsx)(m.Fragment,{children:(0,m.jsxs)("form",{onSubmit:function(e){return e.preventDefault(),console.log("shall edit/create",S),y({profileDef:S},{onSuccess:function(){return v()}}),!1},children:[(0,m.jsxs)("div",{className:"mb-5",children:[(0,m.jsx)("label",{htmlFor:"name",children:(0,s.t)("Name")}),(0,m.jsx)(d.Z,{id:"name",name:"profileName",defaultValue:S.name,onChange:function(e){N((0,r.Z)((0,r.Z)({},S),{},{name:e.target.value}))},required:!0})]}),(0,m.jsxs)("div",{className:"mb-5",children:[(0,m.jsx)("label",{htmlFor:"name",children:(0,s.t)("Description")}),(0,m.jsx)(f.Z,{id:"description",name:"profileDescription",defaultValue:S.description,onChange:function(e){N((0,r.Z)((0,r.Z)({},S),{},{description:e.target.value}))},required:!0})]}),(0,m.jsxs)("div",{className:"-mx-2 py-3 sm:flex sm:flex-row-reverse",children:[(0,m.jsx)(u.Z,{className:"mx-2",state:b,icon:"send",children:t||(0,s.t)("Add Profile")}),(0,m.jsx)(u.Z,{className:"mx-2",type:"secondary",onClick:Z,children:(0,s.t)("Cancel")})]})]})})});return(0,a.createPortal)(C,j)}},1950:function(e,n,t){t.r(n),t.d(n,{default:function(){return M}});var r=t(4165),i=t(5861),o=t(2982),a=t(885),s=t(1803),c=t(2791),l=t(6871),u=t(7221),d=t(3431),f=t(184),p=function(e){var n=e.className,t=e.items,r=e.onChange;return(0,f.jsx)("div",{className:"flex ".concat(n),children:t.map((function(e){var n;return(0,f.jsx)("a",{className:"flex-grow cursor-pointer border-b-2 py-2 px-1 text-lg ".concat(e.isActive?"border-indigo-500 text-indigo-500 dark:text-indigo-400":"border-gray-300 transition-colors duration-300 hover:border-indigo-400 dark:border-gray-800 hover:dark:border-indigo-600"," ").concat(null!==(n=e.className)&&void 0!==n?n:""),onClick:function(){r(e.key)},children:e.title},e.key)}))})},m=t(1413),h=t(4990),x=t(9077),v=t(9072),Z=t(3225),j=t(8491),g=t(4226),y=function(e){var n,t=e.profileId,r=e.sectionId,i=(0,c.useState)(!1),o=(0,a.Z)(i,2),l=o[0],u=o[1],d=(0,c.useState)(),p=(0,a.Z)(d,2),y=p[0],b=p[1],w=(0,x.Z)({}).save,I=w.mutate,S=w.isLoading,N=w.isError,C=w.isSuccess,k=function(){u(!1),b(void 0)};return(0,f.jsx)(f.Fragment,{children:l?(0,f.jsxs)(j.Z,{title:"New".concat(y?":":""," ").concat(null!==(n=null===y||void 0===y?void 0:y.typeDefinition.name)&&void 0!==n?n:""),isOpaqueBg:!0,children:[void 0===y?(0,f.jsxs)("div",{className:"mb-5",children:[(0,f.jsx)("label",{htmlFor:"type",children:(0,h.t)("Attribute Type")}),(0,f.jsxs)(Z.Z,{id:"type",onChange:function(e){!function(e){var n=Object.values(s.H1).find((function(n){return n.type.toString()===e}));b({id:"",type:e,sectionId:r,priority:-1,data:{},typeDefinition:n,profileId:t,acl:{requiredSecurityGroup:s.hh.Owner}})}(e.target.value)},children:[(0,f.jsx)("option",{children:(0,h.t)("Make a selection")}),Object.values(s.H1).map((function(e){return(0,f.jsx)("option",{value:e.type.toString(),children:e.name},e.type.toString())}))]})]}):(0,f.jsx)(g.Z,{attribute:y,onChange:function(e){if(y){var n=(0,m.Z)({},y);n.data[e.target.name]=e.target.value,b(n)}}}),(0,f.jsxs)("div",{className:"flex flex-row",children:[(0,f.jsx)(v.Z,{type:"secondary",className:"ml-auto",onClick:k,children:(0,h.t)("Cancel")}),(0,f.jsx)(v.Z,{type:"primary",icon:"plus",className:"ml-2",onClick:function(){console.log(y),I(y,{onSuccess:function(){k()}})},state:S?"loading":C?"success":N?"error":void 0,children:(0,h.t)("Add")})]})]}):(0,f.jsx)("div",{className:"flex flex-row",children:(0,f.jsx)(v.Z,{type:"primary",icon:"plus",className:"mx-auto min-w-[9rem]",onClick:function(){return u(!0)},children:(0,h.t)("Add Attribute")})})})},b=t(6916),w=t(5207),I=t(3004),S=t(2864),N=t(9636),C=t(4655),k=t(7408),P=t(6117),D=function(e){var n=e.profileId,t=(0,k.useQueryClient)(),o=(0,P.Z)().getSharedSecret,a=new s.KU({api:s.Ii.Owner,sharedSecret:o()}),c=function(){var e=(0,i.Z)((0,r.Z)().mark((function e(n){var t,i;return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:if(t=n.profileId){e.next=3;break}return e.abrupt("return",[]);case 3:return e.next=5,a.profileDefinitionProvider.getProfileSections(t);case 5:return i=e.sent,e.abrupt("return",i);case 7:case"end":return e.stop()}}),e)})));return function(n){return e.apply(this,arguments)}}(),l=function(){var e=(0,i.Z)((0,r.Z)().mark((function e(n){var t,i;return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return t=n.profileId,i=n.profileSection,e.next=3,a.profileDefinitionProvider.saveProfileSection(t,i);case 3:return e.abrupt("return",e.sent);case 4:case"end":return e.stop()}}),e)})));return function(n){return e.apply(this,arguments)}}();return{fetchAll:(0,k.useQuery)(["profileSections",n],(function(){return c({profileId:n})}),{refetchOnMount:!1,refetchOnWindowFocus:!1}),save:(0,k.useMutation)(l,{onSuccess:function(e,n){t.invalidateQueries(["profileSections",n.profileId])},onError:function(e){console.error(e)}})}},O=function(e){var n=e.section,t=e.profileId,r=e.onClose,i=(0,c.useState)((0,m.Z)({},n)),o=(0,a.Z)(i,2),s=o[0],l=o[1],u=D({}).save,d=u.mutateAsync,p=u.status;return(0,f.jsx)(j.Z,{title:"".concat((0,h.t)("Edit"),": ").concat(n.name),isOpaqueBg:!0,children:(0,f.jsxs)("form",{onSubmit:function(e){e.preventDefault(),d({profileId:t,profileSection:s},{onSuccess:function(){r()}})},children:[(0,f.jsxs)("div",{className:"mb-5",children:[(0,f.jsx)("label",{htmlFor:"name",children:(0,h.t)("Name")}),(0,f.jsx)(b.Z,{id:"name",name:"name",defaultValue:n.name,onChange:function(e){var n=(0,m.Z)({},s);n[e.target.name]=e.target.value,l(n)}})]}),(0,f.jsxs)("div",{className:"flex flex-row",children:[(0,f.jsx)(v.Z,{type:"secondary",className:"ml-auto",onClick:function(e){e.preventDefault(),r()},children:(0,h.t)("Cancel")}),(0,f.jsx)(v.Z,{type:"primary",className:"ml-2",state:p,children:(0,h.t)("Save")})]})]})})},F=function(e){var n=e.profileId,t=e.onCreate,l=D({profileId:n}),u=l.fetchAll.data,d=l.save,p=d.mutateAsync,m=d.status,x=(0,c.useState)(""),Z=(0,a.Z)(x,2),g=Z[0],y=Z[1],w=function(){var e=(0,i.Z)((0,r.Z)().mark((function e(i){var a,c;return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return i.preventDefault(),a=s.j4.getNewId(),c={sectionId:a,attributes:[],priority:null!==u&&void 0!==u&&u.length?Math.max.apply(Math,(0,o.Z)(u.map((function(e){return e.priority}))))+1:1,isSystemSection:!1,name:g},e.next=5,p({profileId:n,profileSection:c});case 5:t(a);case 6:case"end":return e.stop()}}),e)})));return function(n){return e.apply(this,arguments)}}();return(0,f.jsx)(j.Z,{title:"New: section",isOpaqueBg:!0,children:(0,f.jsxs)("form",{onSubmit:w,children:[(0,f.jsxs)("div",{className:"mb-5",children:[(0,f.jsx)("label",{htmlFor:"name",children:(0,h.t)("Name")}),(0,f.jsx)(b.Z,{id:"name",name:"sectionName",onChange:function(e){y(e.target.value)},required:!0})]}),(0,f.jsx)("div",{className:"flex flex-row",children:(0,f.jsx)(v.Z,{className:"ml-auto",state:m,children:(0,h.t)("Add section")})})]})})},A=function(e){var n=e.section,t=e.profileId,r=(0,u.Z)({profileId:t,sectionId:n.sectionId}),i=(0,a.Z)(r,1)[0],s=i.data,l=i.isLoading,d=(0,c.useState)(!1),p=(0,a.Z)(d,2),m=p[0],x=p[1];if(!s||l)return(0,f.jsx)(f.Fragment,{children:"Loading"});var Z=s.reduce((function(e,n){return-1!==e.indexOf(n.type)?e:[].concat((0,o.Z)(e),[n.type])}),[]).map((function(e){var n=s.filter((function(n){return n.type===e}));return{name:n[0].typeDefinition.name,attributes:n}}));return(0,f.jsxs)("div",{className:"pt-5",children:[n?m?(0,f.jsx)(O,{section:n,profileId:t,onClose:function(){return x(!1)}},n.sectionId):(0,f.jsxs)("section",{className:"items-center bg-slate-50 p-3 dark:bg-slate-800 sm:flex sm:flex-row",children:[(0,f.jsx)("p",{className:"sm:mr-2",children:n.name}),(0,f.jsx)(v.Z,{type:"secondary",className:"ml-auto",onClick:function(){return x(!0)},children:(0,h.t)("Edit Section")})]}):null,s.length?Z.map((function(e){return(0,f.jsx)(S.Z,{groupTitle:e.name,attributes:e.attributes},e.name)})):(0,f.jsx)("div",{className:"py-5",children:(0,h.t)("section-empty-attributes")}),(0,f.jsx)(y,{profileId:t,sectionId:n.sectionId})]})},M=function(){var e=(0,d.Z)().fetchProfiles,n=e.data,t=e.isLoading,r=(0,l.UO)().profileKey,i=(0,c.useState)(!1),s=(0,a.Z)(i,2),u=s[0],m=s[1],x=null===n||void 0===n?void 0:n.definitions.find((function(e){return e.slug===r})),Z=D({profileId:null===x||void 0===x?void 0:x.profileId}).fetchAll.data,j=(0,c.useState)(null!==Z&&void 0!==Z&&Z.length?Z[0].sectionId:""),g=(0,a.Z)(j,2),y=g[0],b=g[1];if(t)return(0,f.jsx)(f.Fragment,{children:"Loading"});if(!n)return(0,f.jsx)(f.Fragment,{children:(0,h.t)("no-data-found")});if(!x)return(0,f.jsx)(f.Fragment,{children:"Incorrect profile path"});var S="new"===y||!(null!==Z&&void 0!==Z&&Z.length),k=S?void 0:Z.find((function(e){return e.sectionId===y}))||Z[0],P=null!==Z&&void 0!==Z&&Z.length?Z.map((function(e,n){return{title:e.name,key:e.sectionId,isActive:y?y===e.sectionId:0===n}})):[];return(0,f.jsxs)(f.Fragment,{children:[(0,f.jsx)(I.Z,{icon:C.Z,title:x.name,actions:(0,f.jsx)(f.Fragment,{children:(0,f.jsx)(v.Z,{onClick:function(){return m(!0)},children:(0,h.t)("Edit Profile")})}),breadCrumbs:[{href:"/owner/profile",title:"My Profiles"},{title:r}]}),(0,f.jsx)(p,{className:"mt-5",items:[].concat((0,o.Z)(P),[{title:(0,f.jsx)(w.Z,{className:"h-5 w-5"}),key:"new",isActive:y?"new"===y:!(null!==Z&&void 0!==Z&&Z.length),className:"flex-grow-0"}]),onChange:function(e){b(e)}}),S?(0,f.jsx)(F,{profileId:x.profileId,onCreate:function(e){return b(e)}}):k&&(0,f.jsx)(A,{section:k,profileId:x.profileId},k.sectionId),(0,f.jsx)(N.Z,{isOpen:u,title:(0,h.t)("Edit Profile: ")+x.name,confirmText:(0,h.t)("Save"),defaultValue:x,onCancel:function(){m(!1)},onConfirm:function(){m(!1)}})]})}},3990:function(e,n,t){t.d(n,{P:function(){return i},V:function(){return r}});var r=function(e){return e.split(" ").join("-").toLowerCase()},i=function(e){return Object.keys(e).map((function(n){return n+"="+e[n]})).join("&")}},3431:function(e,n,t){var r=t(4165),i=t(1413),o=t(5861),a=t(7408),s=t(1803),c=t(3990),l=t(6117);n.Z=function(){var e=(0,a.useQueryClient)(),n=(0,l.Z)().getSharedSecret,t=new s.KU({api:s.Ii.Owner,sharedSecret:n()}),u=function(){var e=(0,o.Z)((0,r.Z)().mark((function e(){var n;return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return e.next=2,t.profileDefinitionProvider.getProfileDefinitions();case 2:return e.next=4,e.sent.map((function(e){return(0,i.Z)((0,i.Z)({},e),{},{slug:(0,c.V)(e.name)})}));case 4:return n=e.sent,e.abrupt("return",{definitions:n});case 6:case"end":return e.stop()}}),e)})));return function(){return e.apply(this,arguments)}}(),d=function(){var e=(0,o.Z)((0,r.Z)().mark((function e(n){var i;return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return i=n.profileDef,e.next=3,t.profileDefinitionProvider.saveProfileDefinition(i);case 3:return e.abrupt("return",e.sent);case 4:case"end":return e.stop()}}),e)})));return function(n){return e.apply(this,arguments)}}();return{fetchProfiles:(0,a.useQuery)(["profiles"],(function(){return u()}),{refetchOnMount:!1,refetchOnWindowFocus:!1}),saveProfile:(0,a.useMutation)(d,{onSuccess:function(){e.invalidateQueries(["profiles"])},onError:function(e){console.error(e)}})}}}}]);
//# sourceMappingURL=31.a9be5641.chunk.js.map