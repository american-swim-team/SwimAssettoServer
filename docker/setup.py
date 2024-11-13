import os
import subprocess
from dotenv import load_dotenv

# Load environment variables from .env file
load_dotenv()

def get_env_variable(var_name):
    try:
        return os.environ[var_name]
    except KeyError:
        raise EnvironmentError(f"Environment variable '{var_name}' not found")

def configure_s3cmd(access_key, secret_key, linode_endpoint):
    s3cfg_content = f"""
[default]
access_key = {access_key}
secret_key = {secret_key}
host_base = {linode_endpoint}
host_bucket = {bucket_name}
use_https = True
"""
    with open('s3cfg', 'w') as s3cfg_file:
        s3cfg_file.write(s3cfg_content)
    print("s3cmd configured successfully.")

def download_files_from_s3(bucket_name, subfolder, local_dir):
    if not os.path.exists(local_dir):
        os.makedirs(local_dir)
    
    cmd = f"s3cmd -c s3cfg sync s3://{bucket_name}/{subfolder}/content/ {local_dir}/content/"
    subprocess.run(cmd, shell=True, check=True)
    print(f"Files downloaded from s3://{bucket_name}/{subfolder} to {local_dir}")

def launch_application(app_path):
    print(f'Launching application: {app_path}')
    os.execvp(app_path, [app_path])

if __name__ == '__main__':
    # Retrieve settings from environment variables
    try:
        bucket_name = get_env_variable('LINODE_BUCKET')
        subfolder = get_env_variable('LINODE_SUBFOLDER')
        local_dir = get_env_variable('LOCAL_DIR')
        linode_endpoint = get_env_variable('LINODE_ENDPOINT')
        access_key = get_env_variable('LINODE_ACCESS_KEY')
        secret_key = get_env_variable('LINODE_SECRET_KEY')
        try:
            application_path = get_env_variable('APPLICATION_PATH')
        except EnvironmentError:
            application_path = "/app/AssettoServer"
    except EnvironmentError:
        print("Environment variable not found! Launching app...")
        launch_application("/app/AssettoServer")

    configure_s3cmd(access_key, secret_key, linode_endpoint)
    download_files_from_s3(bucket_name, subfolder, local_dir)
    launch_application(application_path)
