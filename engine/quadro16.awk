awk '
function get_param(){
DIR="/home/mariusz/bin/"
PI = atan2(1,1) * 4;DEC2RAD=PI/180;RAD2DEC=180/PI
ALPHA="ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz"
bracket1="([{<";bracket2=")]}>"
SKOM1="lol=0.01 upl=0.02 weight=10"
DOT_SYMBOLS=".([{<>}])"
FI_TETRAD_LIB=DIR"tetrad16.lib";FO_INIT="init.cya"
AL["r"]=-51.673;BE["r"]=174.807;GA["r"]=41.685;DE["r"]=79.130;CH["r"]=-157.210;EP["r"]=-147.839;ZE["r"]=-75.097
N0["r"]=8.240;N1["r"]=-33.549;N2["r"]=44.091;N3["r"]=-40.207;N4["r"]=20.597;P["r"]=7.977;VMAX["r"]=44.522
#AL["d"]=-29.887;BE["d"]=136.341;GA["d"]=31.143;DE["d"]=143.370;CH["d"]=-97.982;EP["d"]=-140.769;ZE["d"]=-160.498
#N0["d"]=-33.004;N1["d"]=44.723;N2["d"]=-40.207;N3["d"]=23.060;N4["d"]=5.920;P["d"]=153.912;VMAX["d"]=44.767
#AL["d"]=-51.673;BE["d"]=174.807;GA["d"]=41.685;DE["d"]=79.130;CH["d"]=-157.210;EP["d"]=-147.839;ZE["d"]=-75.097
#N0["d"]=8.240;N1["d"]=-33.549;N2["d"]=44.091;N3["d"]=-40.207;N4["d"]=20.597;P["d"]=7.977;VMAX["d"]=44.522

AL["d"]=-63.00;BE["d"]=171.000;GA["d"]=54.000;DE["d"]=123.000;CH["d"]=-117.000;EP["d"]=-169.000;ZE["d"]=-108.000
N0["d"]=8.240;N1["d"]=-33.549;N2["d"]=44.091;N3["d"]=-40.207;N4["d"]=20.597;P["d"]=7.977;VMAX["d"]=44.522

HO=-101.50
DE["N"]= 83.443;N1["N"]=-25.784;N2["N"]= 37.221
DE["S"]=144.462;N1["S"]= 33.352;N2["S"]=-34.469
CH["A"]=-165.768;CH["S"]=40.00;iteration=300
test="n";rm_level=5;err=0;libp=0;libpr=0;lih=0;lihr=0
wsk_lih=0;wsk_lihr=0
}

function new_x(x1,y1,cfi,sfi){return (cfi*x1-sfi*y1)}
function new_y(x1,y1,cfi,sfi){return (sfi*x1+cfi*y1)}
function new_z(z1,shift){return z1+shift}

function chack_input(){k2max=0;li=0
if(err>0){return err}
if(name==""){name="quadro7_test"}
foi_xplor2=name"_xolor2.inp"

#Sprawdzenie sekwencji----------------------------------------------------
if(seq==""){err=1;printf "ERROR %d : Brak sekwencji\n",err;return err}
lseq=length(seq);li_rna=0;li_dna=0
 for(i=1;i<=lseq;i++){s=substr(seq,i,1);coto[i]="."
  if(index("ACGTU",s)>0){li_rna=li_rna+1;res_rna[li_rna]=i;coto[i]="r";typ="r"}else{
  if(index("acgtu",s)>0){li_dna=li_dna+1;res_dna[li_dna]=i;coto[i]="d";typ="d"}else{
    err=2;printf "ERROR %d : Nieprawidłowa reszta %s%d w sekwencji\n",err,s,i;return err}
                                                                                   }
VAL[i]=AL[typ];VBE[i]=BE[typ];VGA[i]=GA[typ];VDE[i]=DE[typ];VCH[i]=CH[typ];VEP[i]=EP[typ];VZE[i]=ZE[typ];VN1[i]=N1[typ];VN2[i]=N2[typ]
if(coto[i]=="r"){VHO[i]=HO}
#printf "%s VDE[%d]= %8.3f\n",substr(seq,i,1),i,VDE[i]
                     }
# printf "li_rna= %d li_dna= %d lseq= %d\n",li_rna,li_dna,lseq
if((li_rna+li_dna)!=lseq){err=102;printf "ERROR %d : Nieprawidłowa sekwencja\n",err;return err}
if(li_rna==lseq){typ_seq="RNA";udzial_rna=100.00}else{if(li_dna==lseq){typ_seq=="DNA";udzial_rna=0.00}else{typ_seq="CHIMERA";udzial_rna=li_rna*100/lseq}}
# printf "typ_seq= %s udzial RNA= %6.2f\n",typ_seq,udzial_rna

#Sprawdzenie topologii----------------------------------------------------
if(dot==""){err=3;printf "ERROR %d : Brak topologii\n",err;return err}
if(lseq!=ldot){err=4;printf "ERROR %d : różna długość sekwencji i topologii (%d != %d)\n",err,lseq,ldot;return err}
if(index(dot,"^")>0){typ_dot=1}else{typ_dot=0}
ntetrad=0;li=0
if(typ_dot==0){
 for(i=1;i<=ldot;i++){s=substr(dot,i,1)
 if(index(DOT_SYMBOLS,s)==0){k=index(ALPHA,s);if((k>0)&&(k<27)){li=li+1;po[li]=i}else{
 err=104;printf "ERROR %d blad w topologii w pozycji %d\n",err,i;return err}}
                     }
 if(li!=npath){err=105;printf "ERROR %dniezgodnosc topologii i path\n",err,i;return err}
 for(i=1;i<=li;i++){
 if(substr(PATH[i],1,1)!=substr(dot,po[i],1)){
 err=105;printf "ERROR %dniezgodnosc topologii i path\n",err,i;return err}
                   } 
                 }else{
 for(i=1;i<=ldot;i++){s=substr(dot,i,1)
 if(substr(dot,i,1)=="^"){li=li+1;po[li]=i}else{if(index(ALPHA,s)>0){
 err=104;printf "ERROR %d blad w topologii w pozycji %d\n",err,i;return err}}
                     }
 if(li!=npath){err=105;printf "ERROR %dniezgodnosc topologii i path\n",err,i;return err}
 for(i=1;i<=li;i++){s1=substr(dot,1,po[i]-1)""substr(PATH[i],1,1)""substr(dot,po[i]+1);dot=s1}
                      }
if((li%4)!=0){err=104;printf "ERROR %d blad w topologi\n",err;return err}
ntetrad=li/4;lip=li
# printf "ntetrad= %d\n",ntetrad
if(po[li]<lseq){lip=lip+1;po[lip]=ldot;printf "po[%d]= %d ldot= %d\n",lip,po[lip],ldot}
for(i=1;i<=ldot;i++){
    s1=substr(seq,i,1);s1a=toupper(s1);k1=index("GUT",s1a)
    s2=substr(dot,i,1);s2a=toupper(s2);k2=index(ALPHA,s2a)
    if((k2>0)&&(k1>0)){if(k2>k2max){k2max=k2};li=li+1;seq_tetr[k2]=seq_tetr[k2]""s1}else{
      if((k2>0)&&(k1==0)){err=5;printf "ERROR %d : raszta %s%d powinna byc G|g|U|u|T|t\n",err,i,s1}
                                                                                 }
                    }
for(i=1;i<=ldot;i++){
  s=substr(dot,i,1);k=index(bracket1,s)
  if(k>0){z1=substr(bracket1,k,1);z2=substr(bracket2,k,1);h=1;j=i
  while((h>0)&&(j<ldot)){j=j+1
   s1=substr(dot,j,1);if(s1==z1){h=h+1}else{if(s1==z2){h=h-1}}
                        }
  if(h==0){libp=libp+1;bp0[libp]=k;bp1[libp]=i;bp2[libp]=j}else{
    err=105;printf "ERROR %d blad w topologi\n",err;return err}
        }else{k=index(bracket2,s)
  if(k>0){z1=substr(bracket1,k,1);z2=substr(bracket2,k,1);h=-1;j=i
  while((h<0)&&(j>1)){j=j-1
   s1=substr(dot,j,1);if(s1==z1){h=h+1}else{if(s1==z2){h=h-1}}
                     }
  if(h==0){libpr=libpr+1;bp_rev0[libpr]=k
     bp_rev1[libpr]=i;bp_rev2[libpr]=j
          }else{
   err=105;printf "ERROR %d blad w topologi\n",err;return err
               }
         }   }      }
if(libp!=libpr){err=105;printf "blad w strukturze\n"}
if(libp>0){
for(i=1;i<=libp;i++){s1=(substr(seq,bp1[i],1));s2=(substr(seq,bp2[i],1))
s=s1""s2;k=index("atgcgta",s);if(k!=0){typ_bp[i]=s}else{
k=index("AUGCGUATGTA",s);if(k!=0){typ_bp[i]=s}else{
err=106;printf "ERROR %d bledna para\n",err}}
printf "%2d %s%d %s%d typ_bp[%d]= %s\n",i,substr(seq,bp1[i],1),bp1[i],substr(seq,bp2[i],1),bp2[i],i,typ_bp[i]
                    }
if(libpr>0){
for(i=1;i<=libpr;i++){s1=(substr(seq,bp_rev1[i],1));s2=(substr(seq,bp_rev2[i],1))
s=s1""s2;k=index("atgcgtaugua",s);if(k!=0){typ_bpr[i]=s}else{
k=index("AUGCGUA",s);if(k!=0){typ_bpr[i]=s}else{
err=106;printf "ERROR %d bledna para\n",err}}
printf "%2d %s%d %s%d typ_bpr[%d]= %s\n",i,substr(seq,bp_rev1[i],1),bp_rev1[i],substr(seq,bp_rev2[i],1),bp2[i],i,typ_bpr[i]
                    }
           }
lih=1;i=1;helis1[lih]=bp1[i];helis2[lih]=bp2[i];lhelis[lih]=1
for(i=2;i<=libp;i++){
  if((bp1[i]-bp1[i-1]==1)&&(bp2[i-1]-bp2[i]==1)){lhelis[lih]=lhelis[lih]+1}else{
  lih=lih+1;helis1[lih]=bp1[i];helis2[lih]=bp2[i];lhelis[lih]=1}
                    }

lihr=1;i=1;helisr1[lihr]=bp_rev1[i];helisr2[lihr]=bp_rev2[i];lhelisr[lihr]=1
for(i=2;i<=libpr;i++){
  if((bp_rev1[i]-bp_rev1[i-1]==1)&&(bp_rev2[i-1]-bp_rev2[i]==1)){lhelisr[lihr]=lhelisr[lihr]+1}else{
  lihr=lihr+1;helisr1[lihr]=bp_rev1[i];helisr2[lihr]=bp_rev2[i];lhelisr[lihr]=1}
                    }

          }
#sprawdzenie chi i shugar ---------------------------------------------------
if(chi==""){lchi=lseq;for(i=1;i<=lchi;i++){chi=chi"."}}
if(lchi!=lseq){err=17; printf "ERROR nieprawidłowa długość parametru chi\n";return err}
# wprowadzenie poprawki parametru chi 25.03.26 w zleżności od par WC
if(libp>0){
for(i=1;i<=libp;i++){
if(toupper(substr(chi,bp1[i],1))!="S"){s=substr(chi,1,bp1[i]-1)"A"substr(chi,bp1[i]+1);chi=s}
if(toupper(substr(chi,bp2[i],1))!="S"){s=substr(chi,1,bp2[i]-1)"A"substr(chi,bp2[i]+1);chi=s}
                    }
printf "nowe chi %s\n",chi
          }
for(i=1;i<=lchi;i++){s=substr(chi,i,1);CHIFIX[i]=0;if(index(".AaSs",s)==0){err=18;printf "ERROR Błąd parametru chi w pozycji %d %s\n",i,s;return err}
if(s!="."){typ=toupper(s);VCH[i]=CH[typ];if(index("AS",s)>0){CHIFIX[i]=1}}
# printf "TUTAJ %s %s VCH[%d]= %8.3f CHIFIX[%d]= %8.3f\n",substr(seq,i,1),substr(chi,i,1),i,VCH[i],d,CHIFIX[i]
                    }
if(shugar==""){lshugar=lseq;for(i=1;i<=lshugar;i++){shugar=shugar"."}}
if(lshugar!=lseq){err=19;printf "ERROR nieprawidłowa długość parametru shugar\n";return err}
for(i=1;i<=lshugar;i++){s=substr(shugar,i,1);SHUGARFIX[i]=0;if(index(".NnSs",s)==0){err=20;printf "ERROR Błąd parametru shugar w pozycji %d %s\n",i,s;return err}
if(s!="."){typ=toupper(s);VDE[i]=DE[typ];VN1[i]=N1[typ];VN2[i]=N2[typ];if(index("NS",s)>0){SHUGARFIX[i]=1}}
#printf "TUTAJ_93 %s %s VDE[%d]= %8.3f SHUGARFIX[%d]= %8.3f\n",substr(seq,i,1),substr(shugar,i,1),i,VDE[i],i,SHUGARFIX[i]
                    }

#Sprawdzenie orientacji-------------------------------------------------------
if(norient!=ntetrad){err=11;printf "ERROR %d : Błedna sorient\n",err; return err}
for(i=1;i<=norient;i++){
  if(substr(ALPHA,i,1)!=substr(ORIENT[i],1,1)){err=12;printf "ERROR %d : Błedny orient\n",err; return err}
  if((substr(ORIENT[i],2,1)!="+")&&(substr(ORIENT[i],2,1)!="-")){err=13;printf "ERROR %d : Błedna sorient\n",err; return err}
                       }

if(err==0){
 for(i=1;i<=ntetrad;i++){s1=substr(seq_tetr[i],1,1);s2=substr(ORIENT[i],2,1)
#  printf "TUTAJ_105 %d %s %s\n",i,seq_tetr[i],ORIENT[i]
  if(i==1){if(index("GUT",s1)>0){nazwa_lib[i]="R"s1"4"}else{nazwa_lib[i]="D"toupper(s1)"4"}
   if(s2=="+"){nazwa_lib[i]=nazwa_lib[i]"P"}else{nazwa_lib[i]=nazwa_lib[i]"M"}
          }else{
  nazwa_lib[i]=nazwa_lib[i]=toupper(s1)"4"
  if(s2=="+"){nazwa_lib[i]=nazwa_lib[i]"P"}else{nazwa_lib[i]=nazwa_lib[i]"M"}
               }         
  for(j=1;j<=nlib;j++){if(nazwa_lib[i]==name_lib_res[j]){numer_lib[i]=j}}
#  print "TUTAJ_113",nazwa_lib[i],numer_lib[i]
                       }
start[1]=0;start[2]=last_atom_lib_res[numer_lib[1]]-1
for(i=2;i<=ntetrad;i++){start[i+1]=start[i]+n_atoms_lib_res[numer_lib[i]]}
n_atoms_first=0;n_angles_first=0;first_atom=0;last_atom=0
for(i=1;i<=ntetrad;i++){
 n_atoms_first=n_atoms_first+n_atoms_lib_res[numer_lib[i]]
 n_angles_first=n_angles_first+n_angles_lib_res[numer_lib[i]]
                       }
if(length(name)>5){s=substr(name,1,5)}else{s=name}
#printf "TUTAJ_123 RESIDUE   %-5s%5d%5d%5d%5d\n",s,n_angles_first,n_atoms_first,first_atom_lib_res[numer_lib[1]],n_atoms_first-1
          }

#Sprawdzenie rise i twist-------------------------------------------------------
nrise=split(srise,RISE,";");if(nrise<ntetrad){for(i=nrise+1;i<ntetrad;i++){RISE[i]=RISE[1]}}
ntwist=split(stwist,TWIST,";");if(ntwist<ntetrad){for(i=ntwist+1;i<ntetrad;i++){TWIST[i]=TWIST[1]}}
rise_sum[1]=0;twist_sum[1]=0;cfitwist[1]=1.0;sfitwist[1]=0.0
for(i=2;i<=ntetrad;i++){
  rise_sum[i]=rise_sum[i-1]+RISE[i-1]
  twist_sum[i]=twist_sum[i-1]+TWIST[i-1]
  cfitwist[i]=cos(DEC2RAD*twist_sum[i])
  sfitwist[i]=sin(DEC2RAD*twist_sum[i])
                       }
#for(i=1;i<=ntetrad;i++){printf "rise_sum[%d]= %6.2f RISE[%s]= %6.2f\n",i,rise_sum[i],i,RISE[i]}

#Sprawdzenie path-----------------------------------------------------------------
npath=split(spath,VPATH,";");if(ntetrad*4!=npath){err=14;printf "ERROR %d : Błedny path\n",err}else{
#for(i=1;i<=npath;i++){printf "%2d %s\n",i,VPATH[i]}
li=0;
for(i=1;i<=ldot;i++){s1=substr(dot,i,1);k=index(ALPHA,s1);if(k!=0){li=li+1;NPATH[VPATH[li]]=i}}
                                                                                                   }
n=0
for(i=1;i<=ldot;i++){s=substr(dot,i,1);if(index(DOT_SYMBOLS,s)==0){n=n+1;po[n]=i}}
for(i=1;i<=n;i++){printf "po[%d]= %d\n",i,po[i]>"matrix.txt"}
for(i=1;i<=npath;i++){
n=index(ALPHA,substr(VPATH[i],1,1));stetr[n]=stetr[n]""substr(seq,po[i],1)
matrix[n][length(stetr[n])]=po[i]
                     }
for(i=1;i<=ntetrad;i++){s=toupper(stetr[i])
# printf "TUTAJ_152 %s\n",stetr[i]
 if((s!="GGGG")&&(s!="UUUU")&&(s!="TTTT")){err=15;printf "ERROR %d : Błedny path\n",err}
                       }
k=0;for(i=1;i<=ntetrad;i++){for(j=1;j<=4;j++){k=k+1;printf " %4d %2s",matrix[i][j],VPATH[k]>"matrix.txt"};printf "\n">"matrix.txt"}
#for(i=1;i<=ntetrad;i++){printf "TUTAJ_156 %d %s %s\n",i,seq_tetr[i],stetr[i]}
}


function get_tetrad_lib(){
 nlib=0
 while(getline<FI_TETRAD_LIB>0){
  if($1=="RESIDUE"){nlib=nlib+1
   name_lib_res[nlib]=$2
   n_angles_lib_res[nlib]=$3
   n_atoms_lib_res[nlib]=$4
   first_atom_lib_res[nlib]=$5
   last_atom_lib_res[nlib]=$6
   for(i=1;i<=n_angles_lib_res[nlib];i++){
    getline<FI_TETRAD_LIB
    name_angle_lib[nlib][i]=$2
    a1_lib[nlib][i]=$6
    a2_lib[nlib][i]=$7
    a3_lib[nlib][i]=$8
    a4_lib[nlib][i]=$9
    a5_lib[nlib][i]=$10;
                                         }
   for(i=1;i<=n_atoms_lib_res[nlib];i++){
    getline<FI_TETRAD_LIB
    name_atom_lib[nlib][i]=$2
    typ_atom_lib[nlib][i]=$3
    x_atom_lib[nlib][i]=$6
    y_atom_lib[nlib][i]=$7
    z_atom_lib[nlib][i]=$8
    b1_lib[nlib][i]=$9
    b2_lib[nlib][i]=$10
    b3_lib[nlib][i]=$11
    b4_lib[nlib][i]=$12
    b5_lib[nlib][i]=$13
                                        }
                    }
                               }
 close(FI_TETRAD_LIB)
}

function print_tetrad_lib(){
 for(i=1;i<=nlib;i++){printf "name_lib_res[%d]= %s\n",i,name_lib_res[i]
  for(j=1;j<=n_angles_lib_res[i];j++){
   printf "TUTAJ_199 name_angle_lib[%2d][%2d]= %s",i,j,name_angle_lib[i][j]
   if(a1_lib[i][j]==0){s1="...."}else{s1=name_atom_lib[i][a1_lib[i][j]]}
   if(a2_lib[i][j]==0){s2="...."}else{s2=name_atom_lib[i][a2_lib[i][j]]}
   if(a3_lib[i][j]==0){s3="...."}else{s3=name_atom_lib[i][a3_lib[i][j]]}
   if(a4_lib[i][j]==0){s4="...."}else{s4=name_atom_lib[i][a4_lib[i][j]]}
   if(a5_lib[i][j]==0){s5="...."}else{s5=name_atom_lib[i][a5_lib[i][j]]}
   printf "    %-5s %-5s %-5s %-5s %-5s\n",s1,s2,s3,s4,s5
                                     }
}}

function create_def_angles(){la=0;tetr="A"
 for(i=1;i<n_angles_lib_res[numer_lib[1]];i++){la=la+1
  angle[la]=name_angle_lib[numer_lib[1]][i]
  if(substr(angle[la],1,2)=="CH"){angle[la]=substr(angle[la],1,2)""tetr""index("ABCD",substr(angle[la],3,1))}
   angle_def[la][1]=a1_lib[numer_lib[1]][i]
   angle_def[la][2]=a2_lib[numer_lib[1]][i]
   angle_def[la][3]=a3_lib[numer_lib[1]][i]
   angle_def[la][4]=a4_lib[numer_lib[1]][i]
   angle_def[la][5]=a5_lib[numer_lib[1]][i]
   if(angle_def[la][5]==start[2]){angle_def[la][5]=start[ntetrad+1]}
   if(angle_def[la][4]>start[2]){angle_def[la][4]=start[ntetrad+1]+1}
   if(i==n_angles_lib_res[numer_lib[1]]-1){angle_def[la][5]=angle_def[la][4]}
                                              }
 for(j=2;j<=ntetrad;j++){tetr=substr(ALPHA,j,1)
  for(i=1;i<=n_angles_lib_res[numer_lib[j]];i++){la=la+1
  g=index("ABCD",substr(name_angle_lib[numer_lib[j]][i],3,1))
  angle[la]=substr(name_angle_lib[numer_lib[j]][i],1,2)""tetr""g
  angle_def[la][1]=a1_lib[numer_lib[j]][i]+start[j]
  angle_def[la][2]=a2_lib[numer_lib[j]][i]+start[j]
  angle_def[la][3]=a3_lib[numer_lib[j]][i]+start[j]
  angle_def[la][4]=a4_lib[numer_lib[j]][i]+start[j]
  angle_def[la][5]=a5_lib[numer_lib[j]][i]+start[j]
                                                }
                        }
 i=numer_lib[1];j=n_angles_lib_res[i];la=la+1
 angle[la]=name_angle_lib[i][j]
 angle_def[la][1]=a1_lib[i][j]
 angle_def[la][2]=a2_lib[i][j]
 angle_def[la][3]=start[ntetrad+1]+1
 angle_def[la][4]=start[ntetrad+1]+2
 angle_def[la][5]=0
}

function print_def_angles(){
 for(i=1;i<=la;i++){
  printf "TUTUJ_244 %4d %-5s    0    0    0.0000",i,angle[i]
  for(ii=1;ii<=5;ii++){printf "%5d",angle_def[i][ii]};printf "\n"
}}

function write_def_angles(){
 for(i=1;i<=la;i++){
  printf "%4d %-5s    0    0    0.0000",i,angle[i]>"Q.lib"
  for(ii=1;ii<=5;ii++){printf "%5d",angle_def[i][ii]>"Q.lib"};printf "\n">"Q.lib"
}}

function print_header(){s="Q"
printf "RESIDUE   %-5s%5d%5d%5d%5d\n",s,n_angles_first,n_atoms_first,first_atom_lib_res[numer_lib[1]],n_atoms_first-1
}

function write_header(){s="Q"
printf "RESIDUE   %-5s%5d%5d%5d%5d\n",s,n_angles_first,n_atoms_first,first_atom_lib_res[numer_lib[1]],n_atoms_first-1>>"Q.lib"
}

function create_def_atoms(){
 lb=0;tetr="A"
 for(i=1;i<last_atom_lib_res[numer_lib[1]];i++){lb=lb+1
  ss=name_atom_lib[numer_lib[1]][i]
  if(ss=="HO2"){ss="HO\047"}
  ss1=substr(ss,3,1);col=index("ABCD",ss1);ss2=substr(ss,1,2)
  if(col>0){ss=ss2""tetr""col}
  sstyp=typ_atom_lib[numer_lib[1]][i];ss2=substr(ss,1,2);if((ss2=="O\047")||(ss2=="C\047")){sstyp="DUMMY"}
  atom[lb]=ss;atom_typ[lb]=sstyp
  atom_x[lb]=x_atom_lib[numer_lib[1]][i]
  atom_y[lb]=y_atom_lib[numer_lib[1]][i]
  atom_z[lb]=z_atom_lib[numer_lib[1]][i]
  atom_def[lb][1]=b1_lib[numer_lib[1]][i]
  atom_def[lb][2]=b2_lib[numer_lib[1]][i]
  atom_def[lb][3]=b3_lib[numer_lib[1]][i]
  atom_def[lb][4]=b4_lib[numer_lib[1]][i]
  atom_def[lb][5]=b5_lib[numer_lib[1]][i]
                                               }
 for(j=2;j<=ntetrad;j++){k=numer_lib[j];tetr=substr(ALPHA,j,1)
  for(i=1;i<=n_atoms_lib_res[k];i++){lb=lb+1
   ss=name_atom_lib[k][i];ss1=substr(ss,3,1);col=index("ABCD",ss1);ss2=substr(ss,1,2)
   sstyp=typ_atom_lib[numer_lib[j]][i];if((ss2=="O\047")||(ss2=="C\047")){sstyp="DUMMY"}
   atom[lb]=ss2""tetr""col;atom_typ[lb]=sstyp
   atom_x[lb]=new_x(x_atom_lib[numer_lib[j]][i],y_atom_lib[numer_lib[j]][i],cfitwist[j],sfitwist[j])
   atom_y[lb]=new_y(x_atom_lib[numer_lib[j]][i],y_atom_lib[numer_lib[j]][i],cfitwist[j],sfitwist[j])
   atom_z[lb]=new_z(z_atom_lib[numer_lib[j]][i],rise_sum[j])
   for(ii=1;ii<=5;ii++){atom_def[lb][ii]=0}
   if(b1_lib[numer_lib[j]][i]!=0){atom_def[lb][1]=b1_lib[numer_lib[j]][i]+start[j]}
   if(b2_lib[numer_lib[j]][i]!=0){atom_def[lb][2]=b2_lib[numer_lib[j]][i]+start[j]}
   if(b3_lib[numer_lib[j]][i]!=0){atom_def[lb][3]=b3_lib[numer_lib[j]][i]+start[j]}
   if(b4_lib[numer_lib[j]][i]!=0){atom_def[lb][4]=b4_lib[numer_lib[j]][i]+start[j]}
                                    }
                        }
 lb=lb+1;i=last_atom_lib_res[numer_lib[1]]
 atom[lb]=name_atom_lib[numer_lib[1]][i];atom_typ[lb]="CT"
 atom_x[lb]=x_atom_lib[numer_lib[1]][i]
 atom_y[lb]=y_atom_lib[numer_lib[1]][i]
 atom_z[lb]=z_atom_lib[numer_lib[1]][i]
 atom_def[lb][1]=b1_lib[numer_lib[1]][i];atom_def[lb][2]=lb+1
 for(ii=3;ii<=5;ii++){atom_def[lb][ii]=0}
 lb=lb+1;i=i+1;
 atom[lb]=name_atom_lib[numer_lib[1]][i];atom_typ[lb]="P"
 atom_x[lb]=x_atom_lib[numer_lib[1]][i]
 atom_y[lb]=y_atom_lib[numer_lib[1]][i]
 atom_z[lb]=z_atom_lib[numer_lib[1]][i]
 atom_def[lb][1]=lb-1;
 for(ii=2;ii<=5;ii++){atom_def[lb][ii]=0}
}

function print_def_atoms(){
 for(i=1;i<=lb;i++){
 printf "%4d %-5s%-5s    0    0.0000%10.4f%10.4f%10.4f",i,atom[i],atom_typ[i],atom_x[i],atom_y[i],atom_z[i]
 for(ii=1;ii<=5;ii++){printf "%5d",atom_def[i][ii]};printf "\n"
                   }
}

function write_def_atoms(){
 for(i=1;i<=lb;i++){
# if(index(atom[i],"PA1")>0){printf "ERRR= %d %d |%s|\n",k,i,atom[i]}
 if(atom[i]=="PA1"){atom[i]="P"}
 printf "%4d %-5s%-5s    0    0.0000%10.4f%10.4f%10.4f",i,atom[i],atom_typ[i],atom_x[i],atom_y[i],atom_z[i]>>"Q.lib"
 for(ii=1;ii<=5;ii++){printf "%5d",atom_def[i][ii]>>"Q.lib"};printf "\n">>"Q.lib"
                   }}


function write_VTOR(){
for(i=1;i<=lseq;i++){
printf "%s %8.3f %8.3f %8.3f %8.3f %8.3f %8.3f %8.3f %8.3f %8.3f",substr(seq,i,1),VAL[i],VBE[i],VGA[i],VDE[i],VEP[i],VZE[i],VCH[i],VN1[i],VN2[i]>"t_tor.txt"
if(coto[i]=="r"){printf " %8.3f\n",VHO[i]>"t_tor.txt"}else{printf "\n">"t_tor.txt"}
}
}

function set_angle_fix_nawiasow_otwartych(){
if(wsk_lih>0){
for(gg=1;gg<=wsk_lih;gg++){
printf "angle fix \042* %d..%d\042\n",helis1[gg],helis1[gg]+lhelis[gg]-1 > FO_INIT
}}}

function set_angle_fix_nawiasow_zamknietych(){
if(wsk_lihr>0){
k1=helisr1[wsk_lihr];k2=helisr2[wsk_lihr]
#for(gg=1;gg<=wsk_lihr;gg++){
gg=wsk_lihr
printf "angle fix \042* %d..%d\042\n",helisr1[gg],helisr1[gg]+lhelisr[gg]-1 > FO_INIT
printf "angle free \042??A %d..%d\042\n",helisr2[gg]-lhelisr[gg]+1,helisr2[gg] > FO_INIT
for(g=0;g<lhelisr[wsk_lihr];g++){
k3=k1+g;k4=k2-g
printf "distance make \042PA %d\042 \042P %d\042 lol=0.01 upl=0.10 weight=10\n",k4,k3 > FO_INIT
printf "distance make \042O5\\\O47A %d\042 \042O5\\\O47 %d\042 lol=0.01 upl=0.10 weight=10\n",k4,k3 > FO_INIT
printf "distance make \042C5\\\O47A %d\042 \042C5\\\O47 %d\042 lol=0.01 upl=0.10 weight=10\n",k4,k3 > FO_INIT
printf "distance make \042C4\\\O47A %d\042 \042C4\\\O47 %d\042 lol=0.01 upl=0.10 weight=10\n",k4,k3 > FO_INIT
printf "distance make \042C3\\\O47A %d\042 \042C3\\\O47 %d\042 lol=0.01 upl=0.10 weight=10\n",k4,k3 > FO_INIT
printf "distance make \042O3\\\O47A %d\042 \042O3\\\O47 %d\042 lol=0.01 upl=0.10 weight=10\n",k4,k3 > FO_INIT
#printf "minimize %d\n",iteration > FO_INIT
                                }
#printf "minimize %d\n",iteration > FO_INIT
printf "angle fix \042??A %d..%d\042\n",helisr2[gg]-lhelisr[gg]+1,helisr2[gg] > FO_INIT
#                           }
}}

function czy_sa_nawiasy_otwarte(z1,z2){
odp=0
while((helis1[wsk_lih+1]>=z1)&&(helis1[wsk_lih+1]<=z2)&&(wsk_lih<=lih)){
odp=odp+1;wsk_lih=wsk_lih+1}
if(wsk_lih>lih){wsk_lih=wsk_lih-1}
printf "------------ wsk_lih= %d zakres: %d %d\n",wsk_lih,z1,z2
set_angle_fix_nawiasow_otwartych()
return odp
}

function czy_sa_nawiasy_zamkniete(z1,z2){
odp=0
while((helisr1[wsk_lihr+1]>=z1)&&(helisr1[wsk_lihr+1]<=z2)&&(wsk_lihr<=lihr)){
odp=odp+1;wsk_lihr=wsk_lihr+1}
if(wsk_lihr>lihr){wsk_lihr=wsk_lihr-1}
printf "++++++++++++ wsk_lihr= %d zakres: %d %d\n",wsk_lihr,z1,z2
set_angle_fix_nawiasow_zamknietych()
return odp
}


function print_init_cya(){m1=1
fo_agnles_cya=name"_angles.cya"; system("cp "DIR"ryb.cya "fo_agnles_cya)
first=NPATH[VPATH[1]]
if(my_angles!=""){
#system("cat "sryb" >> "fo_agnles_cya)
while(getline<my_angles>0){
  for(i=1;i<(NF-1);i=i+3){
if($i=="AL"){VAL[$(i+1)]=strtonum($(i+2))}
if($i=="BE"){VBE[$(i+1)]=strtonum($(i+2))}
if($i=="GA"){VGA[$(i+1)]=strtonum($(i+2))}
if($i=="DE"){VDE[$(i+1)]=strtonum($(i+2))}
if($i=="N1"){VN1[$(i+1)]=strtonum($(i+2))}
if($i=="N2"){VN2[$(i+1)]=strtonum($(i+2))}
if($i=="EP"){VEP[$(i+1)]=strtonum($(i+2))}
if($i=="ZE"){VZE[$(i+1)]=strtonum($(i+2))}
if($i=="CH"){VCH[$(i+1)]=strtonum($(i+2))}
                         }
                          }
                 }
printf "read lib Q.lib\nread seq Q%d\n",first > FO_INIT
n=ntetrad*4
for(i=1;i<first;i++){
printf "angle set \042AL* %d\042 %8.3f\nangle set \042BE* %d\042 %8.3f\n",i,VAL[i],i,VBE[i] > FO_INIT
printf "angle set \042GA* %d\042 %8.3f\nangle set \042DE* %d\042 %8.3f\n",i,VGA[i],i,VDE[i] > FO_INIT
if(i>1){printf "angle set \042EP* %d\042 %8.3f\n",i-1,VEP[i-1] > FO_INIT}
printf "angle set \042ZE* %d\042 %8.3f\n",i,VZE[i] > FO_INIT
printf "angle set \042N1* %d\042 %8.3f\nangle set \042N2* %d\042 %8.3f\n",i,VN1[i],i,VN2[i] > FO_INIT
printf "angle set \042CH* %d\042 %8.3f\n",i,VCH[i] > FO_INIT
if(coto[i]=="r"){printf "angle set \042HO* %d\042 %8.3f\n",i,VHO[i] > FO_INIT}
if(CHIFIX[i]==1){printf "angle fix \042CH* %d\042\n",i > FO_INIT}
if(SHUGARFIX[i]==1){printf "angle fix \042DE* %d\042\n",i > FO_INIT
                    printf "angle fix \042N1* %d\042\n",i > FO_INIT
                    printf "angle fix \042N2* %d\042\n",i > FO_INIT
                   }
#printf "distance make \042O4\\\047 %d\042 \042C4\\\047 %d\042 lol=1.30 upl=1.60 weight=10\n",i,i > FO_INIT
#printf "distance make \042C1\\\047 %d\042 \042C4\\\047 %d\042 lol=2.10 upl=2.58 weight=10\n",i,i > FO_INIT
#printf "distance make \042O4\\\047 %d\042 \042H4\\\047 %d\042 lol=1.93 upl=2.37 weight=10\n",i,i > FO_INIT
#printf "distance make \042O4\\\047 %d\042 \042C3\\\047 %d\042 lol=2.08 upl=2.55 weight=10\n",i,i > FO_INIT
#printf "distance make \042O4\\\047 %d\042 \042C5\\\047 %d\042 lol=2.08 upl=2.55 weight=10\n",i,i > FO_INIT
                    }
czy_sa_nawiasy_otwarte(1,first)
czy_sa_nawiasy_zamkniete(1,first)

i=first
printf "angle set \042AL* %d\042 %8.3f\nangle set \042BE* %d\042 %8.3f\n",i,VAL[i],i,VBE[i] > FO_INIT
printf "angle set \042GA* %d\042 %8.3f\nangle set \042DE* %d\042 %8.3f\n",i,VGA[i],i,VDE[i] > FO_INIT
if(i>1){printf "angle set \042EP* %d\042 %8.3f\n",i-1,VEP[i-1] > FO_INIT}
printf "angle set \042ZE* %d\042 %8.3f\n",i,VZE[i] > FO_INIT
printf "angle set \042N1* %d\042 %8.3f\nangle set \042N2* %d\042 %8.3f\n",i,VN1[i],i,VN2[i] > FO_INIT
printf "angle set \042CH* %d\042 %8.3f\n",i,VCH[i] > FO_INIT
if(CHIFIX[i]==1){printf "angle fix \042CH* %d\042\n",i > FO_INIT}
if(coto[i]=="r"){printf "angle set \042HO* %d\042 %8.3f\n",i,VHO[i] > FO_INIT}
if(SHUGARFIX[i]==1){printf "angle fix \042DE* %d\042\n",i > FO_INIT
                    printf "angle fix \042N1* %d\042\n",i > FO_INIT
                    printf "angle fix \042N2* %d\042\n",i > FO_INIT
                   }
for(j=1;j<=lip;j++){
printf "angle set \042CH%s %d\042 %8.3f\n",VPATH[j],first,VCH[po[j]] > FO_INIT
if(CHIFIX[po[j]]==1){printf "angle fix \042CH%s %d\042\n",VPATH[j],first > FO_INIT}
                   }
printf "%smp_makeaco \042CH\052 DE\052 N\052 + 1..%d\042\n",DIR,first > FO_INIT
#printf "%smp_makeaco \042DE\052 1..%d\042\n",DIR,first > FO_INIT
#printf "%smp_makeaco \042N\052 1..%d\042\n",DIR,first > FO_INIT
#czy_sa_nawiasy_otwarte(1,first)
#czy_sa_nawiasy_zamkniete(1,first)
printf "minimize %d\n",iteration > FO_INIT
if(test=="y"){printf "write temperary%d.pdb\n",first > FO_INIT}
#printf "angle free\n" > FO_INIT
printf "write ang Q%d fixed\n",first > FO_INIT
#DOTAD JEST DOBRZE
for(j=2;j<=lip;j++){
printf "read seq Q%d append\n",po[j] > FO_INIT
printf "read ang Q%d\n",po[j-1] > FO_INIT
printf "%smp_sugarbond 1..%d\n",DIR,po[j] > FO_INIT
for(i=1;i<=po[j];i++){
if(i==first){
printf "distance make \042O4\\\047 %d\042 \042C4\\\047 %d\042 lol=1.30 upl=1.60 weight=10\n",i,i > FO_INIT
printf "distance make \042C1\\\047 %d\042 \042C4\\\047 %d\042 lol=2.10 upl=2.58 weight=10\n",i,i > FO_INIT
printf "distance make \042O4\\\047 %d\042 \042H4\\\047 %d\042 lol=1.93 upl=2.37 weight=10\n",i,i > FO_INIT
printf "distance make \042O4\\\047 %d\042 \042C3\\\047 %d\042 lol=2.08 upl=2.55 weight=10\n",i,i > FO_INIT
printf "distance make \042O4\\\047 %d\042 \042C5\\\047 %d\042 lol=2.08 upl=2.55 weight=10\n",i,i > FO_INIT
           }
#else{printf "%ssugarbond 1..%d\n",DIR,i > FO_INIT}
                     }
for(i=po[j-1];i<po[j];i++){
if(i!=po[j-1]){
printf "angle set \042AL* %d\042 %8.3f\nangle set \042BE* %d\042 %8.3f\n",i,VAL[i],i,VBE[i] > FO_INIT
printf "angle set \042GA* %d\042 %8.3f\nangle set \042DE* %d\042 %8.3f\n",i,VGA[i],i,VDE[i] > FO_INIT
printf "angle set \042N1* %d\042 %8.3f\nangle set \042N2* %d\042 %8.3f\n",i,VN1[i],i,VN2[i] > FO_INIT
printf "angle set \042CH* %d\042 %8.3f\n",i,VCH[i] > FO_INIT
}
if(i>1){printf "angle set \042EP* %d\042 %8.3f\nangle set \042ZE* %d\042 %8.3f\n",i-1,VEP[i-1],i,VZE[i] > FO_INIT}
if(CHIFIX[i]==1){printf "angle fix \042CH* %d\042\n",i > FO_INIT}
if(coto[i]=="r"){printf "angle set \042HO* %d\042 %8.3f\n",i,VHO[i] > FO_INIT}
if(CHIFIX[i]==1){printf "angle fix \042CH* %d\042\n",i > FO_INIT}
if(SHUGARFIX[i]==1){printf "angle fix \042DE* %d\042\n",i > FO_INIT
                    printf "angle fix \042N1* %d\042\n",i > FO_INIT
                    printf "angle fix \042N2* %d\042\n",i > FO_INIT
                   }
                          }
i=po[j]
printf "angle set \042AL* %d\042 %8.3f\nangle set \042BE* %d\042 %8.3f\n",i,VAL[i],i,VBE[i] > FO_INIT
printf "angle set \042GA* %d\042 %8.3f\nangle set \042DE* %d\042 %8.3f\n",i,VGA[i],i,VDE[i] > FO_INIT
if(i>1){printf "angle set \042EP* %d\042 %8.3f\nangle set \042ZE* %d\042 %8.3f\n",i-1,VEP[i-1],i,VZE[i] > FO_INIT}
printf "angle set \042N1* %d\042 %8.3f\nangle set \042N2* %d\042 %8.3f\n",i,VN1[i],i,VN2[i] > FO_INIT
printf "angle set \042CH* %d\042 %8.3f\n",i,VCH[i] > FO_INIT
if(CHIFIX[i]==1){printf "angle fix \042CH* %d\042\n",i > FO_INIT}
if(coto[i]=="r"){printf "angle set \042HO* %d\042 %8.3f\n",i,VHO[i] > FO_INIT}
if(SHUGARFIX[i]==1){printf "angle fix \042DE* %d\042\n",i > FO_INIT
                    printf "angle fix \042N1* %d\042\n",i > FO_INIT
                    printf "angle fix \042N2* %d\042\n",i > FO_INIT
                   }
#printf "%smp_makeaco \042\052 %d\042\n",DIR,i > FO_INIT
for(m=j;m<=lip;m++){
printf "angle set \042CH%s %d\042 %8.3f\n",VPATH[m],first,VCH[po[m]] > FO_INIT
if(CHIFIX[po[m]]==1){printf "angle fix \042CH%s %d\042\n",VPATH[m],first > FO_INIT}
                   }
for(m=1;m<=j;m++){
i=po[m];res_name=substr(seq,i,1);s=substr(dot,i,1)
if((res_name=="G")||(res_name=="A")||(res_name=="g")||(res_name=="a")){
printf "distance make \042C4  %d\042 \042C4%s %d\042 %s\n",i,VPATH[m],first,SKOM1 > FO_INIT
printf "distance make \042N9  %d\042 \042N9%s %d\042 %s\n",i,VPATH[m],first,SKOM1 > FO_INIT
                                                                      }
if((res_name=="C")||(res_name=="U")||(res_name=="T")||(res_name=="c")||(res_name=="u")||(res_name=="t")){
printf "distance make \042C4  %d\042 \042C2%s %d\042 %s\n",i,VPATH[m],first,SKOM1 > FO_INIT
printf "distance make \042N9  %d\042 \042N1%s %d\042 %s\n",i,VPATH[m],first,SKOM1 > FO_INIT
                                                                                                        }
printf "distance make \042C1\\\047  %d\042 \042C\\\047%s %d\042 %s\n",i,VPATH[m],first,SKOM1 > FO_INIT
printf "distance make \042O4\\\047  %d\042 \042O\\\047%s %d\042 %s\n",i,VPATH[m],first,SKOM1 > FO_INIT
                 }
if(test=="y"){printf "write temperary%dA.pdb\n",po[j] > FO_INIT}
#printf "minimize %d\n",i*2 > FO_INIT
#printf "%smp_makeaco \042\052 %d..%d\042\n",DIR,po[j-1],po[j] > FO_INIT
printf "%smp_makeaco \042CH\052 DE\052 N\052 + %d..%d\042\n",DIR,po[j-1],po[j] > FO_INIT
#printf "%smp_makeaco \042\DE* %d..%d\042\n",DIR,po[j-1],po[j] > FO_INIT
#printf "%smp_makeaco \042\N* %d..%d\042\n",DIR,po[j-1],po[j] > FO_INIT
czy_sa_nawiasy_otwarte(po[j-1],po[j])
czy_sa_nawiasy_zamkniete(po[j-1],po[j])
printf "minimize %d\n",iteration > FO_INIT
if(test=="y"){printf "write temperary%dB.pdb\n",po[j] > FO_INIT}
#printf "angle free\n" > FO_INIT
printf "write ang Q%d fixed\n",po[j] > FO_INIT
                   }
printf "minimize 100\n" > FO_INIT
if(test=="y"){printf "write temperary%d.pdb\n",lseq > FO_INIT}
printf "write final_cyana.pdb\n" >  FO_INIT
printf "quit\n" > FO_INIT
}

#function write_seq(){
#for(i=1;i<=ldot;i++){
#  printf "%s",new_res[i]>"Q.seq"
#  if(i%20==0){printf "\n">"Q.seq"}else{printf " ">"Q.seq"}
#                    }
#if(i%20!=0){printf "\n">"Q.seq"}
#}

function write_seq(){
for(i=1;i<=ldot;i++){
res_name=substr(seq,i,1);s=substr(dot,i,1);t=toupper(s);k=index(ALPHA,t);s1=substr(seq,i,1)
if(k==0){if(index("ACGTU",s1)>0){new_res[i]="R"s1}else{if(index("acgtu",s1)>0){new_res[i]="D"toupper(s1)}else{
err=16;printf "ERROR %d Nieprawidlowa reszta %s w sekwencji\n",s1}}
        }else{li_path=li_path+1;if(li_path==1){new_res[i]="Q"}else{
if(index("ACGTU",s1)>0){new_res[i]="R"}else{if(index("acgtu",s1)>0){new_res[i]="D"}else{
err=16;printf "ERROR %d Nieprawidlowa reszta %s w sekwencji\n",s1}}}
}}
if(libp>0){
for(i=1;i<=libp;i++){
  s=substr(new_res[bp1[i]],1,1)""toupper(typ_bp[i])
  new_res[bp1[i]]=s;new_res[bp2[i]]="B"
                    }
          }
for(i=1;i<=ldot;i++){fo="Q"i".seq"
for(j=1;j<=i;j++){
  printf "%s",new_res[j]>fo
  if(j%20==0){printf "\n">fo}else{printf " ">fo}
                    }
if(i%20!=0){printf "\n">fo}
close(fo)
}}


function cyana2xplor(){
system("cp Q"lseq".seq  t.seq")
print "awk \047BEGIN{liat=0;lires=0" > "g.exe"
print "while(getline<\042t.seq\042>0){" >> "g.exe"
print " for(i=1;i<=NF;i++){lires=lires+1;res[lires]=$i}}" >> "g.exe"
printf "seq= \042%s\042\ndot= \042%s\042\nspath= \042%s\042\n",seq,dot,spath >> "g.exe"
print "npath=split(spath,VPATH,\042;\042)" >> "g.exe"
printf "libp= %d\n",libp >> "g.exe"
for(i=1;i<=libp;i++){printf "revbp2[%d]=%d\n",bp2[i],bp1[i] >> "g.exe"}
print "}\n{" >> "g.exe"
print "if($1==\042ATOM\042){liat=liat+1;n=strtonum(substr($0,23,4))" >> "g.exe"
print " if(n!=res_prev){lir=lir+1;start[lir]=liat;koniec[lir]=liat;res_prev=n}else{koniec[lir]=liat}" >> "g.exe"
print "   name_at[liat]=substr(\$0,13,5);chain[liat]=substr(\$0,18,5);nres[liat]=strtonum(substr(\$0,23,4));coor[liat]=substr($0,27)" >> "g.exe"
print "}}" >> "g.exe"
print "END{fo=\042t.pdb\042" >> "g.exe"
print "for(i=1;i<=lir;i++){printf \042\%2d \%-4s \%4d \%4d\\n\042,i,res[i],start[i],koniec[i]" >> "g.exe"
print " if(length(res[i])==1){" >> "g.exe"
print "  if(res[i]==\042Q\042){fist=i;lipath=1" >> "g.exe"
print "   for(j=start[i];j<=koniec[i];j++){" >> "g.exe"
print "    sp=substr(name_at[j],4,2);sp1=substr(name_at[j],5,1)" >> "g.exe"
print "    if((sp1==\042 \042)||(sp==\042A1\042)){gsub(\042A1\042,\042  \042,name_at[j])" >> "g.exe"
print " num=num+1;printf \042ATOM\%7d \%s\%s   A\%4d\%s\\n\042,num,name_at[j],substr(seq,nres[j],1),nres[j],coor[j] > fo" >> "g.exe"
print " }}}" >> "g.exe"
print " if((res[i]==\042D\042)||(res[i]==\042R\042)){lipath=lipath+1;ss=VPATH[lipath]" >> "g.exe"
print "  for(j=start[i];j<=koniec[i];j++){" >> "g.exe"
print " num=num+1;printf \042ATOM\%7d \%s\%s   A\%4d\%s\\n\042,num,name_at[j],substr(seq,nres[j],1),nres[j],coor[j] > fo}" >> "g.exe"
print "  for(j=start[fist];j<=koniec[fist];j++){" >> "g.exe"
print "   sp=substr(name_at[j],4,2);sp1=substr(name_at[j],1,3)" >> "g.exe"
print "   if(sp==ss){" >> "g.exe"
print "     if((sp1!=\042 O\\047\042)&&(sp1!=\042 C\\047\042)){" >> "g.exe"
print " num=num+1;printf \042ATOM\%7d \%-5s\%s   A\%4d\%s\\n\042,num,substr(name_at[j],1,3),substr(seq,nres[i],1),i,coor[j] > fo" >> "g.exe"
print "}}}}" >> "g.exe"
print " if(res[i]==\042B\042){" >> "g.exe"
print "  for(j=start[revbp2[i]];j<=koniec[revbp2[i]];j++){" >> "g.exe"
print "   k=index(name_at[j],\042A\042)" >> "g.exe"
print "    if(k>0){" >> "g.exe"
print " num=num+1;printf \042ATOM\%7d \%-5s\%s   A\%4d\%s\\n\042,num,substr(name_at[j],1,k-1),substr(seq,i,1),i,coor[j] > fo" >> "g.exe"
print " }}}" >> "g.exe"
print "    }else{" >> "g.exe"
print " if(length(res[i])==2){" >> "g.exe"
print "  for(j=start[i];j<=koniec[i];j++){" >> "g.exe"
print " num=num+1;printf \042ATOM\%7d \%-5s\%s   A\%4d\%s\\n\042,num,name_at[j],substr(seq,i,1),i,coor[j] > fo}" >> "g.exe"
print "  }else{" >> "g.exe"
print " if(length(res[i])==3){" >> "g.exe"
print "  for(j=start[i];j<=koniec[i];j++){" >> "g.exe"
print "   k=index(name_at[j],\042A\042)" >> "g.exe"
print " num=num+1;printf \042ATOM\%7d \%-5s\%s   A\%4d\%s\\n\042,num,name_at[j],substr(seq,i,1),i,coor[j] > fo" >> "g.exe"
print "}}}}}}\047 final_cyana.pdb" >> "g.exe"
close("g.exe")
system("chmod +x g.exe")
}

function get_noe(){
fo="t.noe"
for(i=1;i<=ntetrad;i++){
stet=substr(ALPHA,i,1);li=0;
 for(j=1;j<=npath;j++){
  if(stet==substr(VPATH[j],1,1)){
   li=li+1;v[strtonum(substr(VPATH[j],2,1))]=po[j]
                                }
                      }
   if(li==4){
     if(substr(ORIENT[i],2,1)=="+"){s="WH"}else{if(substr(ORIENT[i],2,1)=="-"){s="HW"}else{err=20;printf "err=%d ERROR\n",err}}
if(substr(ORIENT[i],2,1)=="+"){
     for(j=1;j<4;j++){
printf "assign (resid %3d and name N2 ) (resid %3d and name N7 ) 2.85 0.15 0.15\n",v[j],v[j+1]>fo
printf "assign (resid %3d and name N1 ) (resid %3d and name O6 ) 2.85 0.15 0.15\n",v[j],v[j+1]>fo
                     }
printf "assign (resid %3d and name N2 ) (resid %3d and name N7 ) 2.85 0.15 0.15\n",v[4],v[1]>fo
printf "assign (resid %3d and name N1 ) (resid %3d and name O6 ) 2.85 0.15 0.15\n",v[4],v[1]>fo
                              }else{
if(substr(ORIENT[i],2,1)=="-"){
     for(j=1;j<4;j++){
printf "assign (resid %3d and name N7 ) (resid %3d and name N2 ) 2.85 0.15 0.15\n",v[j],v[j+1]>fo
printf "assign (resid %3d and name O6 ) (resid %3d and name N1 ) 2.85 0.15 0.15\n",v[j],v[j+1]>fo
                     }
                              }
printf "assign (resid %3d and name N7 ) (resid %3d and name N2 ) 2.85 0.15 0.15\n",v[4],v[1]>fo
printf "assign (resid %3d and name O6 ) (resid %3d and name N1 ) 2.85 0.15 0.15\n",v[4],v[1]>fo
                                   }
            }
                       }
for(i=1;i<=libp;i++){
ss=toupper(typ_bp[i])
if(ss=="GC"){
printf "assign (resid %3d and name N2 ) (resid %3d and name O2 ) 2.85 0.15 0.15\n",bp1[i],bp2[i]>fo
printf "assign (resid %3d and name N1 ) (resid %3d and name N3 ) 2.95 0.15 0.15\n",bp1[i],bp2[i]>fo
printf "assign (resid %3d and name O6 ) (resid %3d and name N4 ) 2.90 0.15 0.15\n",bp1[i],bp2[i]>fo
            }
if(ss=="CG"){
printf "assign (resid %3d and name O2 ) (resid %3d and name N2 ) 2.85 0.15 0.15\n",bp1[i],bp2[i]>fo
printf "assign (resid %3d and name N3 ) (resid %3d and name N1 ) 2.95 0.15 0.15\n",bp1[i],bp2[i]>fo
printf "assign (resid %3d and name N4 ) (resid %3d and name O6 ) 2.90 0.15 0.15\n",bp1[i],bp2[i]>fo
            }
if((ss=="AU")||(ss=="AT")){
printf "assign (resid %3d and name N6 ) (resid %3d and name O4 ) 2.95 0.15 0.15\n",bp1[i],bp2[i]>fo
printf "assign (resid %3d and name N1 ) (resid %3d and name N3 ) 2.80 0.15 0.15\n",bp1[i],bp2[i]>fo
                         }
if((ss=="UA")||(ss=="TA")){
printf "assign (resid %3d and name O4 ) (resid %3d and name N6 ) 2.95 0.15 0.15\n",bp1[i],bp2[i]>fo
printf "assign (resid %3d and name N3 ) (resid %3d and name N1 ) 2.80 0.15 0.15\n",bp1[i],bp2[i]>fo
                         }
if((ss=="GU")||(ss=="GT")){
printf "assign (resid %3d and name O6 ) (resid %3d and name N3 ) 2.90 0.15 0.15\n",bp1[i],bp2[i]>fo
printf "assign (resid %3d and name N1 ) (resid %3d and name O2 ) 2.85 0.15 0.15\n",bp1[i],bp2[i]>fo
                         }
if((ss=="UG")||(ss=="TG")){
printf "assign (resid %3d and name N3 ) (resid %3d and name O6 ) 2.90 0.15 0.15\n",bp1[i],bp2[i]>fo
printf "assign (resid %3d and name O2 ) (resid %3d and name N1 ) 2.85 0.15 0.15\n",bp1[i],bp2[i]>fo
                         }
                   }
if(err==0){close(fo)}
}

function get_planar(){
if(err==0){
fo="t.planar"
for(i=1;i<=ntetrad;i++){printf " group\n  selection=(\n" > fo
  for(j=1;j<=4;j++){
printf "(not( name P   or name O1P or name O2P or name O5\047 or name H5T or\nname H1\047 or name H2\047 or name H3\047 or name H4\047 or name H5\047 or\nname H5\047\047 or name HO2\047 or name C2\047 or name C3\047 or name C4\047 or\nname C5\047 or name O2\047 or name O3\047 or name O4\047 or name H5\042\042\042 or\nname O5\047 or name H2\042\042\042 or name H3T) and residue %d)",matrix[i][j] > fo
if(j<4){printf " or\n" > fo }else{printf "\n" > fo}
                   }
printf ")\nweight=$planarweight\nend\n" > fo
                      }

for(i=1;i<=libp;i++){printf " group\n  selection=(\n" > fo
  printf "(not( name P   or name O1P or name O2P or name O5\047 or name H5T or\nname H1\047 or name H2\047 or name H3\047 or name H4\047 or name H5\047 or\nname H5\047\047 or name HO2\047 or name C2\047 or name C3\047 or name C4\047 or\nname C5\047 or name O2\047 or name O3\047 or name O4\047 or name H5\042\042\042 or\nname O5\047 or name H2\042\042\042 or name H3T) and residue %d) or\n",bp1[i] > fo
printf "(not( name P   or name O1P or name O2P or name O5\047 or name H5T or\nname H1\047 or name H2\047 or name H3\047 or name H4\047 or name H5\047 or\nname H5\047\047 or name HO2\047 or name C2\047 or name C3\047 or name C4\047 or\nname C5\047 or name O2\047 or name O3\047 or name O4\047 or name H5\042\042\042 or\nname O5\047 or name H2\042\042\042 or name H3T) and residue %d)\n",bp2[i] >fo
printf ")\nweight=$planarweight\nend\n" > fo
                   }
close(fo)
         }
}

function get_xplor2(){
if(err==0){
fo="t_xplor2.inp"
printf "parameter @TOPPAR:dna-rna-allatom.param end\nstructure @t1.psf end\nevaluate ($filename=\042t1.pdb\042)\ncoor @$filename end\nenergy end\n" > fo
printf "constraints fix=(( name C1\047 or name N9 or name O6 or name N1 or name N3 or name N7) and (" > fo
for(i=1;i<ntetrad;i++){for(j=1;j<=4;j++){printf " resi %d or ",matrix[i][j]> fo}}
i=ntetrad;for(j=1;j<4;j++){printf " resi %d or ",matrix[i][j]> fo};printf " resi %d )) end\n",matrix[ntetrad][4] > fo
printf "end\n@t.dih\nflags exclude * include bond angl cdih impr vdw noe elec plan end\nminimize powell nstep=1000 nprint=100 end\nevaluate ($filename=\042g.pdb\042)\nwrite coordinates output =$filename end\nenergy end\n" > fo
printf "constraints fix=(not ALL) end\n" > fo
printf "evaluate ($planarweight=20)\n" > fo
printf "restraints planar\n@t.planar\nend\n" > fo
printf "@t.dih\n" > fo
printf "noe\nnres=3000\nclass all\n@t.noe\nend\n" > fo
printf "noe\nceiling=1000\naveraging  * cent\npotential  * square\nsqconstant * 1.\nsqexponent * 2\nscale      * 50.\nend\n" > fo
printf "flags exclude * include bond angl cdih impr vdw noe elec plan end\n" > fo
printf "minimize powell nstep=1000 nprint=100 end\n" > fo
printf "evaluate ($filename=\042%s_xplor.pdb\042)\nwrite coordinates output =$filename end\nenergy end\nstop\n",name > fo
close(fo)
#system("xplor <"fo)
#system("xplor2pdb1.exe g1.pdb >"name".pdb")
        }
}

function rm_files(){
if(rm_level>=5){system("rm matrix.txt g.exe g.aco t_*.inp *_xplor.pdb* t1.* t.* Q.lib Q.seq *_angles.cya init.cya final_cyana.pdb g.pdb* Q*.ang Q*.seq")}
if(rm_level==4){system("rm matrix.txt g.exe t_*.inp *_xplor.pdb* t1.* t.* Q.lib Q.seq *_angles.cya init.cya g.pdb* Q*.ang Q*.seq")}
if(rm_level==3){system("rm matrix.txt g.exe t_*.inp *_xplor.pdb* t1.* t.* g.pdb* Q*.ang Q.seq")}
if(rm_level==2){system("rm matrix.txt g.exe t_*.inp *_xplor.pdb* t1.* t.* g.pdb* *_angles.cya init.cya")}
if(rm_level==1){system("rm matrix.txt g.exe t_*.inp *_xplor.pdb* t1.* t.* g.pdb*")}
}

BEGIN{
 get_param()
 get_tetrad_lib()
# system("cat ATOMTYPES.txt")
# print_tetrad_lib()
}
{
  if($1=="name"){name=$2}
  if($1=="sequence"){seq=$2;lseq=length(seq)}
  if($1=="structure"){dot=$2;ldot=length(dot)}
  if($1=="chi"){chi=$2;lchi=length(chi)}
  if($1=="shugar"){shugar=$2;lshugar=length(shugar)}
  if($1=="orient"){sorient=$2;norient=split(sorient,ORIENT,";")}
  if($1=="rise"){srise=$2;nrise=split(srise,RISE,";")}
  if($1=="twist"){stwist=$2;ntwist=split(stwist,TWIST,";")}
  if($1=="path"){spath=$2;npath=split(spath,PATH,";")}
  if($1=="test"){test=$2}
  if($1=="rm_level"){rm_level=strtonum($2)}
  if($1=="iteration"){iteration=$2;if(iteration<10){err=25;printf "ERROR %d Za mała iteracja %d\n",err,iteration}}
  if($1=="my_angles"){my_angles=$2;
#  if(index($2,".cya")!=length($2)-3){
#   err=16;printf "ERROR %d plik my_angles musi mieć rozszerzenie .cya\n",err
#                                      }else{my_angles=substr($2,1,index($2,".cya")-1)}
                     }
}
END{
chack_input()
if(err==0){
#write_VTOR()
#print ntetrad
#-------------------DEFINICJA KATÓW -----------------------------------------------------
#if(err==0){
create_def_angles()
# print_header();print_def_angles()
system("cat "DIR"ATOMTYPES.txt > Q.lib")
write_header();write_def_angles()
#-------------------DEFINICJA ATOMÓw -----------------------------------------------------
create_def_atoms()
#print_def_atoms()
write_def_atoms();system("cat "DIR"other_residues.lib >> Q.lib")
if(libp>0){system("cat "DIR"bp.lib >> Q.lib")}
print_init_cya()
write_seq()
#-------------------URUCHOMIENIE_CYANY -------------------------------------------------
if(err==0){
system("cyana")
cyana2xplor()
system("./g.exe final_cyana.pdb")
get_noe()
get_planar()
get_xplor2()
          }
system(DIR"cyana2xplor.exe t.pdb > t1.pdb")
system("t2dih.exe")
system("xplor < t_xplor1.inp")
system("xplor < t_xplor2.inp")
system(DIR"xplor2pdb2.exe "name"_xplor.pdb > "name".pdb")
printf "parameter @TOPPAR:dna-rna-allatom.param end\nstructure @t1.psf end\nevaluate ($filename=\042%s_xplor.pdb\042)\ncoor @$filename end\nenergy end\nstop\n",name > "t_xplor3.inp"
system("xplor < t_xplor3.inp > "name"_energy.txt")
#printf "ERROR= %d li_rna= %d li_dna= %d lseq= %d\n",err,li_rna,li_dna,lseq
         }
#printf "lchi= %d\n",lchi
#for(i=1;i<=npath;i++){printf "i= %d NPATH[VPATH[%d]]= %3d VPATH[%d]= %s po[%d]= %2d\n",i,i,NPATH[VPATH[i]],i,VPATH[i],i,po[i]}
rm_files()
printf "libp= %d    libpr= %d\n",libp,libpr
for(i=1;i<=libp;i++){printf "bp1[%d]= %4d bp2[%d]= %4d bp0[%d]= %d bp_rev1[%d]= %4d bp_rev2[%d]= %4d bp_rev0[%d]= %d\n",i,bp1[i],i,bp2[i],i,bp0[i],i,bp_rev1[i],i,bp_rev2[i],i,bp_rev0[i]}
for(i=1;i<=libp;i++){
printf "%2d %s%d %s%d typ_bp[%d]= %s\n",i,substr(seq,bp1[i],1),bp1[i],substr(seq,bp2[i],1),bp2[i],i,typ_bp[i]
                    }
printf "lih= %d  lihr= %d\n",lih,lihr
if(lih==lihr){
for(i=1;i<=lih;i++){
printf "%d. %d - %d  %d - %d len= %d\n",i, helis1[i],helis1[i]+lhelis[i]-1,helis2[i],helis2[i]-lhelis[i]+1,lhelis[i]
                   }
             }
for(i=1;i<=lih;i++){
printf "helis1[%d]= %d    helisr1[%d]= %d   helisr2[%d]= %d  lhelisr[%d]= %d\n",i,helis1[i],i,helisr1[i],i,helisr2[i],i,lhelisr[i]
if(helis1[i]==1){helisa_seq[i]=substr(seq,1,lhelis[i]+1)}else{helisa_seq[i]=substr(seq,helis1[i]-1,lhelis[i]+2)}
printf "%s\n",helisa_seq[i]
                   }
for(i=1;i<=lih;i++){
  i1=helis1[i]-1;i2=helis2[i]+1;ss=""
  for(j=0;j<lhelis[i];j++){
    i1=i1+1;i2=i2-1;ss=ss""substr(seq,i1,1)""substr(seq,i2,1)";"
                          }
  printf "%s\n",substr(ss,1,length(ss)-1)
                   }
for(i=1;i<=ntetrad;i++){printf "TUTAJ_156 %d %s %s\n",i,seq_tetr[i],stetr[i]}
}' $1
