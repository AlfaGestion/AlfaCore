"""Restaura exclusivamente ALFANET_DEMO en SERVER-ALFAVB6. Sin --restaurar solo revisa el plan."""
import argparse
from datetime import datetime
import getpass
import json
import ntpath
import os
from pathlib import Path
import socket
import sys
from consultar_base import consultar

SERVIDOR='SERVER-ALFAVB6'
BASE='ALFANET_DEMO'
RESPALDO=r'E:\MSSQL\BACKUP\ALFANET_DEMO.bak'
DATOS=r'E:\MSSQL\DATA\ALFANET_DEMO.mdf'
LOG=r'E:\MSSQL\DATA\ALFANET_DEMO_log.ldf'

def literal(texto):return "N'"+texto.replace("'","''")+"'"
def sql(sql,base='master'):return consultar(sql,base,silencioso=True)
def objeto(sql_text,base='master'):return json.loads(sql(sql_text,base))[0]
def validar_header(filas):
    if len(filas)!=1:raise ValueError('Se requiere un respaldo completo único, no un archivo con varios juegos.')
    h=filas[0]
    if h.get('DatabaseName')!=BASE or h.get('BackupType')!='1' or h.get('Position')!='1' or h.get('HasBackupChecksums')!='1':
        raise ValueError('El archivo no es el respaldo completo con checksum de ALFANET_DEMO. Se canceló el reinicio.')
    return {k:h[k] for k in ['DatabaseName','BackupSetGUID','BackupFinishDate','Position']}

def validar_archivos(filas):
    if len(filas)!=2 or {f['Type'] for f in filas}!={'D','L'}:raise ValueError('Cambió la distribución de archivos del respaldo.')
    destino={'D':DATOS,'L':LOG}
    for f in filas:
        if ntpath.normcase(ntpath.normpath(f['PhysicalName']))!=ntpath.normcase(destino[f['Type']]):
            raise ValueError('El respaldo no proviene de los archivos físicos exclusivos de ALFANET_DEMO.')
        if not f['LogicalName'] or len(f['LogicalName'])>128:raise ValueError('Nombre lógico de archivo inválido.')
    return filas

def prevalidar():
    estado=objeto("SELECT CONVERT(nvarchar(128),SERVERPROPERTY('ServerName')) servidor,"
        "(SELECT state_desc FROM sys.databases WHERE name=N'ALFANET_DEMO') estado FOR JSON PATH;")
    if estado.get('servidor')!=SERVIDOR or estado.get('estado')!='ONLINE':raise ValueError('Destino incorrecto o base DEMO no disponible.')
    marca=objeto("SELECT COUNT(*) cantidad FROM TA_CONFIGURACION WHERE CLAVE=N'DEMO-BASE-REINICIABLE' AND VALOR=N'ALFANET_DEMO' FOR JSON PATH;",BASE)
    if marca['cantidad']!=1:raise ValueError('La base destino no tiene la marca de demostración esperada.')
    header=validar_header(consultar('RESTORE HEADERONLY FROM DISK='+literal(RESPALDO)+';','master',tabla=True))
    archivos=validar_archivos(consultar('RESTORE FILELISTONLY FROM DISK='+literal(RESPALDO)+' WITH FILE=1;','master',tabla=True))
    actuales=json.loads(sql("SELECT name LogicalName,physical_name PhysicalName,CASE type WHEN 0 THEN 'D' ELSE 'L' END Type "
        "FROM sys.master_files WHERE database_id=DB_ID(N'ALFANET_DEMO') FOR JSON PATH;"))
    validar_archivos(actuales)
    compartidos=objeto("SELECT COUNT(*) cantidad FROM sys.master_files WHERE database_id<>DB_ID(N'ALFANET_DEMO') "
        "AND physical_name IN("+literal(DATOS)+','+literal(LOG)+") FOR JSON PATH;")
    if compartidos['cantidad']:raise ValueError('Los archivos destino están asociados a otra base.')
    sql('RESTORE VERIFYONLY FROM DISK='+literal(RESPALDO)+' WITH FILE=1,CHECKSUM;')
    return {'servidor':SERVIDOR,'base':BASE,'respaldo':RESPALDO,'header':header,'archivos':archivos}

def estado_demo():
    return objeto("SELECT (SELECT COUNT(*) FROM VT_CLIENTES WHERE LTRIM(RTRIM(CODIGO)) LIKE '112019%') clientes_demo,"
        "(SELECT COUNT(*) FROM V_MV_CPTE WHERE SUCURSAL='0099' AND NUMERO BETWEEN '80000001' AND '80999999') comprobantes_demo,"
        "(SELECT COUNT(*) FROM MV_ASIENTOS WHERE SUCURSAL='0099' AND [NUMERO ASIENTO] BETWEEN 80000001 AND 81000999) renglones_demo,"
        "(SELECT COUNT(*) FROM BK_ImportacionExtracto WHERE CuentaBanco IN('111020901','111020902','111010904')) importaciones_demo "
        "FOR JSON PATH;",BASE)

def restaurar(plan):
    moves=','.join('MOVE '+literal(f['LogicalName'])+' TO '+literal(DATOS if f['Type']=='D' else LOG) for f in plan['archivos'])
    # El nombre del destino es constante. No existe argumento para cambiar de base o de servidor.
    sql("""IF DB_NAME()<>N'master' OR CONVERT(nvarchar(128),SERVERPROPERTY('ServerName'))<>N'SERVER-ALFAVB6'
 THROW 51000,'Destino no permitido.',1;
BEGIN TRY
 ALTER DATABASE [ALFANET_DEMO] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
 RESTORE DATABASE [ALFANET_DEMO] FROM DISK="""+literal(RESPALDO)+" WITH FILE=1,"+moves+""",REPLACE,RECOVERY,CHECKSUM;
 ALTER DATABASE [ALFANET_DEMO] SET MULTI_USER;
END TRY
BEGIN CATCH
 IF EXISTS(SELECT 1 FROM sys.databases WHERE name=N'ALFANET_DEMO' AND state_desc='ONLINE')
  ALTER DATABASE [ALFANET_DEMO] SET MULTI_USER;
 THROW;
END CATCH;""")
    resultado=estado_demo()
    if resultado!={'clientes_demo':60,'comprobantes_demo':333,'renglones_demo':679,'importaciones_demo':0}:
        raise RuntimeError('Se restauró la base pero no coincide el lote inicial esperado; revisar el respaldo antes de presentar.')
    return resultado

def registrar_error(ex):
    # Capa común de la herramienta; AUX_ERR de la DEMO y registro local si SQL no está disponible.
    try:
        sql("IF DB_NAME()=N'ALFANET_DEMO' AND CONVERT(nvarchar(128),SERVERPROPERTY('ServerName'))=N'SERVER-ALFAVB6' "
            "AND OBJECT_ID(N'dbo.AUX_ERR',N'U') IS NOT NULL INSERT dbo.AUX_ERR(Proceso,Fecha,Error,Descripcion,Sql,Pc,Usuario) VALUES("
            "N'DEMO_RESTAURAR',GETDATE(),51000,"+literal(str(ex)[:250])+",N'Restablecer exclusivamente ALFANET_DEMO',"
            +literal(socket.gethostname())+",SYSTEM_USER);",BASE)
    except Exception:pass

def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--restaurar',action='store_true');p.add_argument('--confirmar',choices=[BASE])
    p.add_argument('--registro',type=Path,default=Path(__file__).resolve().parent.parent/'Logs')
    a=p.parse_args()
    if not(os.environ.get('ALFACORE_DEMO_SQL_USER') and os.environ.get('ALFACORE_DEMO_SQL_PASSWORD')):
        os.environ['ALFACORE_DEMO_SQL_USER']=input('Usuario SQL [ALFANET]: ').strip() or 'ALFANET'
        os.environ['ALFACORE_DEMO_SQL_PASSWORD']=getpass.getpass('Clave SQL: ')
    registro={'fecha':datetime.now().astimezone().isoformat(),'servidor':SERVIDOR,'base':BASE,'accion':'restaurar' if a.restaurar else 'revisar'}
    try:
        plan=prevalidar();registro['respaldo']=plan['respaldo'];registro['header']=plan['header']
        print('Servidor: '+SERVIDOR+' | Base: '+BASE+' | Respaldo: '+RESPALDO)
        if not a.restaurar:
            print('Plan verificado. Para volver al estado inicial usá --restaurar.');registro['resultado']='PLAN_VALIDADO'
        else:
            print('Se descartarán los cambios posteriores al respaldo en ALFANET_DEMO y se cerrarán sus conexiones.')
            confirmacion=a.confirmar or input('Escribí ALFANET_DEMO para confirmar: ').strip()
            if confirmacion!=BASE:raise ValueError('Restauración cancelada: la confirmación no coincide.')
            registro['estado']=restaurar(plan);registro['resultado']='RESTAURADA'
            print('ALFANET_DEMO restablecida y disponible. Actualizá la pantalla o volvé a conectar AlfaCore.')
    except Exception as ex:
        registro['resultado']='ERROR';registro['error']=str(ex);registrar_error(ex)
        print('No se completó el reinicio: '+str(ex),file=sys.stderr)
        return 1
    finally:
        a.registro.mkdir(parents=True,exist_ok=True)
        nombre=datetime.now().strftime('%Y%m%d_%H%M%S_%f')+'_'+registro['accion']+'.json'
        (a.registro/nombre).write_text(json.dumps(registro,ensure_ascii=False,indent=2),encoding='utf-8')
    return 0

if __name__=='__main__':sys.exit(main())
